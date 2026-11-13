using Rts.GameSession.Actions;
using Rts.GameSession.Configuration;
using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;
using Rts.Shared.World;

namespace Rts.GameSession;

/// <summary>
/// Implementación de <see cref="ISessionHost"/>: el consumidor único de la sesión (07 §7-8, D-082).
/// <para>
/// Cada vuelta del bucle lee <b>un</b> evento del buzón, lo aplica a la máquina de estados, publica el estado nuevo en la vista
/// y ejecuta las acciones en orden. Como es el único que muta el estado, no hace falta ningún lock y no puede asignarse dos veces
/// la misma plaza ni fijarse dos resultados.
/// </para>
/// <para>
/// Lo que puede tardar no bloquea el bucle: detener Simulation y drenar las conexiones corren en otra tarea y vuelven al buzón
/// como eventos (<see cref="SimulationStopped"/>, <see cref="ShutdownCompleted"/>). No toma locks sobre WorldState ni llama a
/// módulos de dominio (07 §8).
/// </para>
/// Se usa una sola vez: para otra partida se lanza el servidor de nuevo (D-058).
/// </summary>
public sealed class SessionHost : ISessionHost
{
  private const int Started = 1;

  private readonly ISessionInbox _inbox;
  private readonly ISessionStateStore _store;
  private readonly ISessionStateMachine _machine;
  private readonly ISessionEffects _effects;
  private readonly ISimulationControl _simulation;
  private readonly TimeProvider _clock;

  // Solo las toca el consumidor: se agregan mientras corre el bucle y se liberan al terminar.
  private readonly List<ITimer> _timers = [];
  private int _started;

  /// <exception cref="ArgumentException">Si la configuración no es válida.</exception>
  public SessionHost(
      ISessionInbox inbox,
      ISessionStateStore store,
      ISessionStateMachine machine,
      ISessionEffects effects,
      ISimulationControl simulation,
      GameSessionConfig config,
      TimeProvider clock)
  {
    _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _machine = machine ?? throw new ArgumentNullException(nameof(machine));
    _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
  }

  /// <inheritdoc/>
  /// <exception cref="InvalidOperationException">Si la sesión ya se ejecutó.</exception>
  public async Task<MatchResult> RunAsync(CancellationToken cancellationToken)
  {
    EnsureRunsOnce();
    using CancellationTokenRegistration shutdown = cancellationToken.Register(RequestShutdown);
    try
    {
      return await ConsumeUntilEndedAsync();
    }
    finally
    {
      DisposeTimers();
    }
  }

  private void EnsureRunsOnce()
  {
    if (Interlocked.Exchange(ref _started, Started) == Started)
    {
      throw new InvalidOperationException("La sesión ya se ejecutó: para otra partida se lanza el servidor de nuevo (D-058).");
    }
  }

  /// <summary>Cancelar el token no corta el bucle: pide el apagado ordenado como un evento más (D-084).</summary>
  private void RequestShutdown() => _inbox.PostInternal(new ShutdownRequested());

  /// <summary>
  /// El bucle del consumidor único. Publicar el estado <b>antes</b> de ejecutar las acciones hace que Networking ya vea, por
  /// ejemplo, <c>AcceptsGameplay = false</c> cuando se pide detener los ticks.
  /// </summary>
  private async Task<MatchResult> ConsumeUntilEndedAsync()
  {
    GameSessionState state = _store.Current;
    while (state.Status != SessionStatus.Ended)
    {
      SessionEvent sessionEvent = await _inbox.ReadAsync(CancellationToken.None);
      SessionTransition transition = _machine.Apply(state, sessionEvent);
      state = transition.State;
      _store.Publish(state);
      Execute(transition.Actions, state.Session);
    }

    return state.Result!;
  }

  /// <summary>Ejecuta las acciones <b>en el orden en que vienen</b>: ese orden es parte del contrato (07 §3, §7).</summary>
  private void Execute(EquatableArray<SessionAction> actions, SessionId session)
  {
    foreach (SessionAction action in actions)
    {
      Execute(action, session);
    }
  }

  /// <summary>Acciones que solo hablan con Networking y State Sync: encolan y vuelven, sin bloquear (08 §5).</summary>
  private void Execute(SessionAction action, SessionId session)
  {
    switch (action)
    {
      case SendSessionMessage send: _effects.Send(send.Connection, send.Message); break;
      case CloseConnection close: _effects.Close(close.Connection, close.Reason); break;
      case PrepareInitialState prepare: PrepareInitial(session, prepare); break;
      default: ExecuteLifecycle(action); break;
    }
  }

  /// <summary>Acciones del ciclo de vida de la partida: timer, ticks, apagado y fin.</summary>
  private void ExecuteLifecycle(SessionAction action)
  {
    switch (action)
    {
      case ScheduleReadyTimeout schedule: ScheduleTimeout(schedule); break;
      case StartSimulation: BeginTicks(); break;
      case StopSimulation: _ = Task.Run(StopSimulationAsync); break;
      case ShutdownConnections: _ = Task.Run(DrainConnectionsAsync); break;
      case CompleteSession: break; // El bucle termina solo al llegar a Ended y devuelve el resultado (D-058).
      default: throw new NotSupportedException($"Acción de sesión no soportada: {action.GetType().Name}.");
    }
  }

  /// <summary>
  /// Pide a Simulation el mundo inicial y se lo entrega a State Sync (07 §5). Si algo falla, no se reintenta: con el mismo mapa y
  /// la misma configuración fallaría igual, así que se informa <see cref="InitializationFailed"/> (D-060).
  /// </summary>
  private void PrepareInitial(SessionId session, PrepareInitialState prepare)
  {
    try
    {
      WorldView world = _simulation.InitializeMatch();
      _effects.PublishInitialState(session, prepare.Initialization, world, prepare.Player1, prepare.Player2);
    }
    catch (Exception exception)
    {
      _inbox.PostInternal(new InitializationFailed(exception.Message));
    }
  }

  /// <summary>
  /// Programa el aviso de vencimiento con el reloj monotónico inyectado, nunca con ticks (07 §4). No se cancela al abortar el
  /// intento: el evento trae su <see cref="InitializationId"/> y la máquina ignora el de un intento que ya no es el vigente.
  /// </summary>
  private void ScheduleTimeout(ScheduleReadyTimeout schedule)
  {
    InitializationId initialization = schedule.Initialization;
    ITimer timer = _clock.CreateTimer(_ => _inbox.PostInternal(new ReadyTimeoutElapsed(initialization)), null, schedule.Delay, Timeout.InfiniteTimeSpan);
    _timers.Add(timer);
  }

  /// <summary>
  /// Arranca el loop de ticks. Que falle no está previsto en el contrato: SUPUESTO TEMPORAL, se informa como falla crítica para que
  /// la sesión termine ordenada en vez de quedar en Running sin ticks. Pendiente de confirmar con Luca (dueño de Simulation).
  /// </summary>
  private void BeginTicks()
  {
    try
    {
      _simulation.StartTicks();
    }
    catch (Exception)
    {
      _inbox.OnSimulationResult(MatchResult.CriticalError(null));
    }
  }

  /// <summary>
  /// Detiene los ticks fuera del consumidor y avisa por el buzón cuando terminó (D-055). Si la detención falla, se informa igual,
  /// sin tick: la sesión no puede quedar esperando para siempre.
  /// </summary>
  private async Task StopSimulationAsync()
  {
    TickId? lastConfirmed;
    try
    {
      lastConfirmed = await _simulation.StopTicksAsync();
    }
    catch (Exception)
    {
      lastConfirmed = null;
    }

    _inbox.PostInternal(new SimulationStopped(lastConfirmed));
  }

  /// <summary>Drena y cierra las conexiones fuera del consumidor. Avisa <see cref="ShutdownCompleted"/> aunque el drenaje falle.</summary>
  private async Task DrainConnectionsAsync()
  {
    try
    {
      await _effects.DrainAndCloseAllAsync();
    }
    catch (Exception)
    {
      // Se ignora: Networking acota el drenaje con su plazo, y la sesión debe terminar igual.
    }
    finally
    {
      _inbox.PostInternal(new ShutdownCompleted());
    }
  }

  private void DisposeTimers()
  {
    foreach (ITimer timer in _timers)
    {
      timer.Dispose();
    }
  }
}
