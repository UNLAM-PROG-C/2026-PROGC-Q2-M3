using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Time;
using Rts.Shared.World;

namespace Rts.Simulation.Loop;

/// <summary>
/// Loop temporizado de Simulation (06 §6). Es lo que ve GameSession (<see cref="ISimulationControl"/>):
/// <list type="bullet">
/// <item><b>Un hilo dedicado</b> llama a <see cref="ISimulationEngine.AdvanceTick"/> cada intervalo (20 Hz). Como hay un solo hilo,
/// nunca hay dos ticks a la vez.</item>
/// <item>Entrega cada publicación a State Sync (<see cref="ITickPublisher"/>). Ante un resultado de gameplay o una falla crítica, avisa a
/// GameSession (<see cref="IMatchResultSink"/>) y termina.</item>
/// <item><see cref="StopTicksAsync"/> nunca corta un tick a la mitad: el hilo revisa el pedido de detención <b>entre</b> ticks. Si el tick
/// en curso produce un resultado, se publica y se avisa <b>antes</b> de completar la detención (D-055).</item>
/// <item><b>Supervisión (06 §6):</b> un temporizador, fuera del hilo del tick, revisa periódicamente cuánto lleva el tick en curso. Si pasa
/// el plazo de seguridad, avisa la falla crítica, completa la detención y abandona ese tick: si algún día termina, se descarta.</item>
/// </list>
/// El fin de la partida se avisa <b>una sola vez</b>, lo detecte el hilo del loop o el supervisor. Los métodos de control pueden llamarse
/// desde cualquier hilo; todo el estado compartido va bajo <c>_gate</c>.
/// </summary>
public sealed class SimulationLoop : ISimulationLoop
{
  private const string ThreadName = "Rts.Simulation.Loop";

  private readonly ISimulationEngine _engine;
  private readonly ITickPublisher _publisher;
  private readonly IMatchResultSink _results;
  private readonly TimeProvider _time;
  private readonly TimeSpan _tickInterval;
  private readonly TimeSpan _stallTimeout;
  private readonly TickSchedule _schedule;
  private readonly object _gate = new();
  private readonly CancellationTokenSource _stopRequested = new();
  private readonly TaskCompletionSource<TickId> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

  // Estado compartido entre GameSession, el hilo del loop y el supervisor: solo se toca bajo _gate.
  private LoopState _state = LoopState.NotStarted;
  private bool _matchEnded;
  private long? _tickStartedAt;
  private TickId _lastConfirmedTick = TickId.Zero;
  private ITimer? _supervisor;

  /// <param name="stallTimeout">
  /// Plazo de seguridad de un tick. Tiene que ser holgado respecto del intervalo: un tick lento es una sobrecarga, no una falla.
  /// </param>
  internal SimulationLoop(
      ISimulationEngine engine,
      ITickPublisher publisher,
      IMatchResultSink results,
      TickRate tickRate,
      TimeSpan stallTimeout,
      TimeProvider time)
  {
    ArgumentNullException.ThrowIfNull(tickRate);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stallTimeout, tickRate.TickInterval);
    _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    _results = results ?? throw new ArgumentNullException(nameof(results));
    _time = time ?? throw new ArgumentNullException(nameof(time));
    _tickInterval = tickRate.TickInterval;
    _stallTimeout = stallTimeout;
    _schedule = new TickSchedule(_tickInterval);
  }

  private enum LoopState
  {
    NotStarted,
    Running,
    Stopped,
  }

  public bool IsRunning
  {
    get
    {
      lock (_gate)
      {
        return _state == LoopState.Running;
      }
    }
  }

  public WorldView InitializeMatch()
  {
    lock (_gate)
    {
      EnsureState(LoopState.NotStarted, "Solo se inicializa la partida antes de arrancar los ticks.");
      return _engine.InitializeMatch();
    }
  }

  public void StartTicks()
  {
    lock (_gate)
    {
      EnsureState(LoopState.NotStarted, "Los ticks ya se arrancaron o se detuvieron.");
      if (_engine.Current is null)
      {
        throw new InvalidOperationException("Falta InitializeMatch antes de arrancar los ticks.");
      }

      _state = LoopState.Running;
      _lastConfirmedTick = _engine.Current.Tick;
      _supervisor = _time.CreateTimer(_ => CheckForStalledTick(), null, _tickInterval, _tickInterval);
      new Thread(Run) { Name = ThreadName, IsBackground = true }.Start();
    }
  }

  public Task<TickId> StopTicksAsync()
  {
    lock (_gate)
    {
      if (_state == LoopState.NotStarted)
      {
        _state = LoopState.Stopped;
        _stopped.TrySetResult(_lastConfirmedTick);
      }

      _stopRequested.Cancel();
      return _stopped.Task;
    }
  }

  /// <summary>Cuerpo del hilo dedicado: tick, entrega, espera hasta el próximo; repite hasta un resultado o un pedido de detención.</summary>
  private void Run()
  {
    long startedAt = _time.GetTimestamp();
    try
    {
      while (TryBeginTick())
      {
        TickOutcome? outcome = AdvanceTickSafely();
        if (!TryFinishTick(outcome) || Deliver(outcome))
        {
          return;
        }

        _schedule.TickFinished(_time.GetElapsedTime(startedAt));
        TimeSpan delay = _schedule.DelayUntilNextStart(_time.GetElapsedTime(startedAt));
        _stopRequested.Token.WaitHandle.WaitOne(delay); // Espera interrumpible: un Stop la corta enseguida.
      }
    }
    finally
    {
      Shutdown();
    }
  }

  /// <summary>
  /// Decide, bajo el mismo lock que <see cref="StopTicksAsync"/>, si arranca otro tick, y anota cuándo empezó para el supervisor.
  /// Un Stop queda ordenado respecto del tick: o llegó antes (y el tick no empieza) o después (y ese tick se termina y se entrega).
  /// </summary>
  private bool TryBeginTick()
  {
    lock (_gate)
    {
      if (_stopRequested.IsCancellationRequested || _matchEnded)
      {
        return false;
      }

      _tickStartedAt = _time.GetTimestamp();
      return true;
    }
  }

  /// <summary>Nulo si el motor lanzó una excepción fuera de sus fases: se trata igual que una falla crítica.</summary>
  private TickOutcome? AdvanceTickSafely()
  {
    try
    {
      return _engine.AdvanceTick();
    }
    catch (Exception)
    {
      return null;
    }
  }

  /// <summary>
  /// Cierra el tick bajo el lock. Falso si el supervisor ya lo abandonó por bloqueado: entonces su resultado se descarta sin publicar.
  /// </summary>
  private bool TryFinishTick(TickOutcome? outcome)
  {
    lock (_gate)
    {
      if (_matchEnded)
      {
        return false;
      }

      _tickStartedAt = null;
      if (outcome?.Publication is { } publication)
      {
        _lastConfirmedTick = publication.Tick;
      }

      return true;
    }
  }

  /// <summary>Publica el tick y, si la partida terminó (resultado o falla), lo avisa. Devuelve verdadero si terminó.</summary>
  private bool Deliver(TickOutcome? outcome)
  {
    if (outcome?.Publication is { } publication)
    {
      try
      {
        _publisher.Publish(publication);
      }
      catch (Exception)
      {
        return EndMatch(MatchResult.CriticalError(publication.Tick));
      }
    }

    if (outcome is null || outcome.Failure is not null)
    {
      return EndMatch(MatchResult.CriticalError(LastConfirmedTick()));
    }

    return outcome.GameplayResult is { } result && EndMatch(result);
  }

  /// <summary>Lo llama el temporizador, en un hilo del pool: si el tick en curso pasó el plazo, termina la partida desde afuera.</summary>
  private void CheckForStalledTick()
  {
    TickId lastConfirmed;
    lock (_gate)
    {
      if (_matchEnded || _tickStartedAt is not { } startedAt || _time.GetElapsedTime(startedAt) <= _stallTimeout)
      {
        return;
      }

      // Detectar y abandonar en la misma sección crítica: el tick ya no puede cerrarse "justo a tiempo" (TryFinishTick lo verá).
      _matchEnded = true;
      lastConfirmed = _lastConfirmedTick;
    }

    // El hilo del tick está bloqueado y no puede avisar: lo hace el supervisor. Primero el aviso, después la detención (D-055).
    _results.OnSimulationResult(MatchResult.CriticalError(lastConfirmed));
    Shutdown();
  }

  /// <summary>Avisa el fin de la partida desde el hilo del loop, <b>una sola vez</b> aunque el supervisor ya lo haya hecho. Siempre verdadero.</summary>
  private bool EndMatch(MatchResult result)
  {
    lock (_gate)
    {
      if (_matchEnded)
      {
        return true;
      }

      _matchEnded = true;
    }

    _results.OnSimulationResult(result); // Fuera del lock: no se llama código ajeno mientras se lo tiene tomado.
    return true;
  }

  /// <summary>Deja el loop detenido y completa la Task de <see cref="StopTicksAsync"/>. Idempotente.</summary>
  private void Shutdown()
  {
    TickId finalTick;
    lock (_gate)
    {
      _state = LoopState.Stopped;
      _supervisor?.Dispose();
      finalTick = _lastConfirmedTick;
    }

    _stopped.TrySetResult(finalTick);
  }

  private TickId LastConfirmedTick()
  {
    lock (_gate)
    {
      return _lastConfirmedTick;
    }
  }

  private void EnsureState(LoopState expected, string message)
  {
    if (_state != expected)
    {
      throw new InvalidOperationException(message);
    }
  }
}
