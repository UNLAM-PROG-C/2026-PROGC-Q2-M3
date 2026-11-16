using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.Simulation.Diagnostics;

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
  private readonly ISimulationDiagnostics _diagnostics;
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
      ISimulationDiagnostics diagnostics,
      TickRate tickRate,
      TimeSpan stallTimeout,
      TimeProvider time)
  {
    ArgumentNullException.ThrowIfNull(tickRate);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stallTimeout, tickRate.TickInterval);
    _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    _results = results ?? throw new ArgumentNullException(nameof(results));
    _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

      _lastConfirmedTick = _engine.Current.Tick;
      StartSupervisorAndThread();
      _state = LoopState.Running;
    }
  }

  /// <summary>
  /// Si falla crear el temporizador o arrancar el hilo, el loop queda detenido (<see cref="StopTicksAsync"/> vuelve enseguida) y la
  /// excepción sigue hacia GameSession, que la informa como falla crítica (D-086). Sin esto, el loop quedaría en Running sin hilo.
  /// </summary>
  private void StartSupervisorAndThread()
  {
    try
    {
      _supervisor = _time.CreateTimer(_ => CheckForStalledTick(), null, _tickInterval, _tickInterval);
      new Thread(Run) { Name = ThreadName, IsBackground = true }.Start();
    }
    catch (Exception)
    {
      _state = LoopState.Stopped;
      _supervisor?.Dispose();
      _stopped.TrySetResult(_lastConfirmedTick);
      throw;
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

  /// <summary>
  /// Cuerpo del hilo dedicado: tick, entrega, diagnóstico y espera hasta el próximo; repite hasta un resultado o un pedido de
  /// detención. El diagnóstico va después de agendar el próximo tick, porque recién ahí se sabe si hubo sobrecarga (D-047); y antes
  /// de terminar, así también se informa el último tick de la partida.
  /// </summary>
  private void Run()
  {
    long startedAt = _time.GetTimestamp();
    try
    {
      while (TryBeginTick())
      {
        TickAttempt attempt = AdvanceTickSafely();
        if (!TryFinishTick(attempt.Outcome))
        {
          return; // El supervisor lo abandonó por bloqueado: la partida ya terminó y no se informa nada más.
        }

        bool matchEnded = Deliver(attempt);
        bool overrun = _schedule.TickFinished(_time.GetElapsedTime(startedAt));
        ReportTick(attempt.Outcome, overrun);
        if (matchEnded)
        {
          return;
        }

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

  /// <summary>
  /// Corre el tick. Si el motor lanzó fuera de sus fases, no hay resultado y se conserva la excepción para informarla (D-099): antes
  /// se descartaba y no quedaba rastro de por qué terminó la partida.
  /// </summary>
  private TickAttempt AdvanceTickSafely()
  {
    try
    {
      return new TickAttempt(_engine.AdvanceTick(), Error: null);
    }
    catch (Exception ex)
    {
      return new TickAttempt(Outcome: null, ex);
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

  /// <summary>
  /// Publica el tick y, si la partida terminó, lo avisa. Devuelve verdadero si terminó. Cada falla crítica sale con su reporte
  /// (D-099): excepción del motor, falla del publicador o falla de una fase.
  /// </summary>
  private bool Deliver(TickAttempt attempt)
  {
    if (attempt.Outcome is not { } outcome)
    {
      TickId lastConfirmed = LastConfirmedTick();
      return EndWithFailure(new CriticalFailureReport(
          CriticalFailureKind.UnexpectedException, lastConfirmed.Next(), lastConfirmed, Phase: null, "El motor lanzó fuera de sus fases.",
          attempt.Error));
    }

    if (outcome.Publication is { } publication && TryPublish(publication) is { } publishError)
    {
      return EndWithFailure(new CriticalFailureReport(
          CriticalFailureKind.PublisherFailed, publication.Tick, publication.Tick, Phase: null, "State Sync no pudo publicar el tick.",
          publishError));
    }

    if (outcome.Failure is { } failure)
    {
      return EndWithFailure(new CriticalFailureReport(
          CriticalFailureKind.PhaseFailed, outcome.Metrics.Tick, LastConfirmedTick(), failure.Phase, failure.Reason, failure.Cause));
    }

    return outcome.GameplayResult is { } result && EndMatch(result);
  }

  /// <summary>Entrega el tick a State Sync. Nulo si salió bien; si no, la excepción, para informarla.</summary>
  private Exception? TryPublish(TickPublication publication)
  {
    try
    {
      _publisher.Publish(publication);
      return null;
    }
    catch (Exception ex)
    {
      return ex;
    }
  }

  /// <summary>
  /// Entrega las métricas del tick al observador, con la sobrecarga que decidió la agenda. No hay métricas si el motor lanzó una
  /// excepción fuera de sus fases. Una falla del observador se ignora: el diagnóstico no es parte del juego (D-095).
  /// </summary>
  private void ReportTick(TickOutcome? outcome, bool overrun)
  {
    if (outcome is not null)
    {
      NotifyDiagnostics(() => _diagnostics.OnTickCompleted(outcome.Metrics with { IsOverrun = overrun }));
    }
  }

  /// <summary>Llama al observador ignorando sus fallas (D-097): el diagnóstico no es parte del juego y la partida sigue.</summary>
  private static void NotifyDiagnostics(Action notify)
  {
    try
    {
      notify();
    }
    catch (Exception)
    {
      // A propósito: un problema al loguear no compromete la consistencia del mundo (AGENTS §6).
    }
  }

  /// <summary>Lo llama el temporizador, en un hilo del pool: si el tick en curso pasó el plazo, termina la partida desde afuera.</summary>
  private void CheckForStalledTick()
  {
    CriticalFailureReport report;
    lock (_gate)
    {
      if (_matchEnded || _tickStartedAt is not { } startedAt)
      {
        return;
      }

      TimeSpan elapsed = _time.GetElapsedTime(startedAt);
      if (elapsed <= _stallTimeout)
      {
        return;
      }

      // Detectar y abandonar en la misma sección crítica: el tick ya no puede cerrarse "justo a tiempo" (TryFinishTick lo verá).
      _matchEnded = true;
      report = StalledTickReport(_lastConfirmedTick, elapsed);
    }

    // El hilo del tick está bloqueado y no puede avisar: lo hace el supervisor. Primero el motivo (D-099), después el resultado y
    // recién al final la detención (D-055).
    NotifyDiagnostics(() => _diagnostics.OnCriticalFailure(report));
    _results.OnSimulationResult(MatchResult.CriticalError(report.LastConfirmedTick));
    Shutdown();
  }

  private CriticalFailureReport StalledTickReport(TickId lastConfirmed, TimeSpan elapsed) => new(
      CriticalFailureKind.TickStalled,
      lastConfirmed.Next(),
      lastConfirmed,
      Phase: null,
      $"El tick lleva {elapsed.TotalMilliseconds:F0} ms en curso, más que TickStallTimeout ({_stallTimeout.TotalMilliseconds:F0} ms).",
      Exception: null);

  /// <summary>Termina la partida por una falla crítica: <c>CriticalError</c> con el último tick confirmado, y el motivo al observador.</summary>
  private bool EndWithFailure(CriticalFailureReport report) =>
      EndMatch(MatchResult.CriticalError(report.LastConfirmedTick), report);

  /// <summary>
  /// Avisa el fin de la partida desde el hilo del loop, <b>una sola vez</b> aunque el supervisor ya lo haya hecho. Si fue una falla,
  /// el motivo va primero al observador y después el resultado a GameSession (D-099). Siempre verdadero.
  /// </summary>
  private bool EndMatch(MatchResult result, CriticalFailureReport? failure = null)
  {
    lock (_gate)
    {
      if (_matchEnded)
      {
        return true;
      }

      _matchEnded = true;
    }

    // Fuera del lock: no se llama código ajeno mientras se lo tiene tomado.
    if (failure is not null)
    {
      NotifyDiagnostics(() => _diagnostics.OnCriticalFailure(failure));
    }

    _results.OnSimulationResult(result);
    return true;
  }

  /// <summary>Un intento de tick: el resultado del motor o, si lanzó fuera de sus fases, la excepción.</summary>
  private readonly record struct TickAttempt(TickOutcome? Outcome, Exception? Error);

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
