using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Loop;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Loop;

public class SimulationLoopTests
{
  private const int FastTicksPerSecond = 200; // 5 ms por tick: las pruebas no dependen de la velocidad, solo del orden.
  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(50);
  private static readonly TimeSpan GenerousStallTimeout = TimeSpan.FromSeconds(2); // Ninguna prueba normal se acerca.
  private static readonly TimeSpan ShortStallTimeout = TimeSpan.FromMilliseconds(100);
  private static readonly TimeSpan SlowTickDuration = TimeSpan.FromMilliseconds(30); // 6 intervalos: sobrecarga segura.
  private static readonly InvalidOperationException PhaseFailureCause = new("Excepción de prueba dentro de una fase.");
  private static readonly InvalidOperationException PublishFailure = new("State Sync roto (prueba).");
  private static readonly TickRate FastRate = new(FastTicksPerSecond);
  private static readonly TickId Tick1 = TickId.Zero.Next();
  private static readonly TickId Tick2 = Tick1.Next();
  private static readonly TickId Tick3 = Tick2.Next();

  private static (SimulationLoop Loop, ScriptedEngine Engine, Recorder Recorder) Create(
      TimeSpan? stallTimeout = null,
      TimeProvider? time = null,
      ISimulationDiagnostics? diagnostics = null)
  {
    var engine = new ScriptedEngine();
    var recorder = new Recorder();
    var loop = new SimulationLoop(
        engine,
        recorder,
        recorder,
        diagnostics ?? NullSimulationDiagnostics.Instance,
        FastRate,
        stallTimeout ?? GenerousStallTimeout,
        time ?? TimeProvider.System);
    loop.InitializeMatch();
    return (loop, engine, recorder);
  }

  [Fact]
  public async Task StalledTick_IsReportedFromOutsideAndItsLateResultIsDiscarded()
  {
    var (loop, engine, recorder) = Create(ShortStallTimeout);
    engine.BlockAt = Tick2;

    loop.StartTicks();
    MatchResult result = await recorder.Result.WaitAsync(TestTimeout); // El tick 2 sigue bloqueado: avisó el supervisor.
    TickId final = await loop.StopTicksAsync().WaitAsync(TestTimeout);
    bool runningAfterStall = loop.IsRunning;

    engine.Release.Set(); // El tick bloqueado termina tarde...
    await Task.Delay(SettleTime);

    Assert.Equal(MatchResult.CriticalError(Tick1), result);
    Assert.Equal(Tick1, final);
    Assert.False(runningAfterStall);
    Assert.Equal(["publish 1", "result CriticalError"], recorder.Entries); // ...y se descarta: no se publica ni se avisa de nuevo.
  }

  [Fact]
  public async Task StopDuringAStalledTick_CompletesWhenTheSupervisorGivesUp()
  {
    var (loop, engine, recorder) = Create(ShortStallTimeout);
    engine.BlockAt = Tick2;

    loop.StartTicks();
    await engine.Blocked.Task.WaitAsync(TestTimeout);
    TickId final = await loop.StopTicksAsync().WaitAsync(TestTimeout); // No se cuelga esperando al tick bloqueado.

    Assert.Equal(Tick1, final);
    Assert.Equal(["publish 1", "result CriticalError"], recorder.Entries); // El aviso llegó antes de completar la detención.
    engine.Release.Set();
  }

  [Fact]
  public void Constructor_StallTimeoutNotAboveTheTickInterval_Throws()
  {
    var recorder = new Recorder();

    Assert.Throws<ArgumentOutOfRangeException>(() =>
        new SimulationLoop(
            new ScriptedEngine(), recorder, recorder, NullSimulationDiagnostics.Instance, FastRate, FastRate.TickInterval, TimeProvider.System));
  }

  [Fact]
  public async Task GameplayResult_IsPublishedThenReportedOnceAndTheLoopEnds()
  {
    var (loop, engine, recorder) = Create();
    engine.EndAt = Tick3;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    TickId final = await loop.StopTicksAsync().WaitAsync(TestTimeout); // Como GameSession al recibir el resultado.

    Assert.Equal(["publish 1", "publish 2", "publish 3", "result BothBasesDestroyed"], recorder.Entries);
    Assert.Equal(Tick3, final);
    Assert.False(loop.IsRunning);
    Assert.False(engine.Overlapped);
  }

  [Fact]
  public async Task CriticalFailure_PublishesNothingForThatTickAndReportsTheLastConfirmedTick()
  {
    var (loop, engine, recorder) = Create();
    engine.FailAt = Tick2;

    loop.StartTicks();
    MatchResult result = await recorder.Result.WaitAsync(TestTimeout);

    Assert.Equal(MatchResult.CriticalError(Tick1), result);
    Assert.Equal(["publish 1", "result CriticalError"], recorder.Entries);
  }

  [Fact]
  public async Task UnexpectedException_IsReportedAsCriticalError()
  {
    var (loop, engine, recorder) = Create();
    engine.ThrowAt = Tick1;

    loop.StartTicks();
    MatchResult result = await recorder.Result.WaitAsync(TestTimeout);

    Assert.Equal(MatchResult.CriticalError(TickId.Zero), result);
  }

  [Fact]
  public async Task StopTicksAsync_StopsTheLoopAndReturnsTheLastConfirmedTick()
  {
    var (loop, engine, recorder) = Create();

    loop.StartTicks();
    await recorder.WaitForPublicationsAsync(2).WaitAsync(TestTimeout);
    TickId final = await loop.StopTicksAsync().WaitAsync(TestTimeout);
    int publishedAtStop = recorder.Entries.Count;
    await Task.Delay(SettleTime);

    Assert.Equal(engine.Current!.Tick, final);
    Assert.Equal(publishedAtStop, recorder.Entries.Count); // No corrió ningún tick más.
    Assert.DoesNotContain(recorder.Entries, entry => entry.StartsWith("result", StringComparison.Ordinal));
    Assert.False(loop.IsRunning);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task StopDuringATick_FinishesAndDeliversThatTickBeforeCompleting(bool tickEndsTheMatch)
  {
    var (loop, engine, recorder) = Create();
    engine.BlockAt = Tick2;
    engine.EndAt = tickEndsTheMatch ? Tick2 : null;

    loop.StartTicks();
    await engine.Blocked.Task.WaitAsync(TestTimeout);
    Task<TickId> stopping = loop.StopTicksAsync();
    bool completedWhileTickRunning = stopping.IsCompleted;
    engine.Release.Set();
    TickId final = await stopping.WaitAsync(TestTimeout);

    Assert.False(completedWhileTickRunning);
    Assert.Equal(Tick2, final);
    string[] expected = tickEndsTheMatch
        ? ["publish 1", "publish 2", "result BothBasesDestroyed"] // D-055: el resultado llega antes de completar la detención.
        : ["publish 1", "publish 2"];
    Assert.Equal(expected, recorder.Entries);
  }

  [Fact]
  public async Task StopTicksAsync_IsIdempotent()
  {
    var (loop, _, _) = Create();

    loop.StartTicks();
    Task<TickId> first = loop.StopTicksAsync();
    Task<TickId> second = loop.StopTicksAsync();

    Assert.Same(first, second);
    await first.WaitAsync(TestTimeout);
  }

  [Fact]
  public async Task StopBeforeStart_ReturnsTickZeroAndForbidsStarting()
  {
    var (loop, _, _) = Create();

    TickId final = await loop.StopTicksAsync().WaitAsync(TestTimeout);

    Assert.Equal(TickId.Zero, final);
    Assert.Throws<InvalidOperationException>(loop.StartTicks);
  }

  [Fact]
  public async Task StartTicks_TwiceOrWithoutInitialize_Throws()
  {
    var recorder = new Recorder();
    var uninitialized = new SimulationLoop(
        new ScriptedEngine(), recorder, recorder, NullSimulationDiagnostics.Instance, FastRate, GenerousStallTimeout, TimeProvider.System);
    Assert.Throws<InvalidOperationException>(uninitialized.StartTicks);

    var (loop, _, _) = Create();
    loop.StartTicks();
    Assert.Throws<InvalidOperationException>(loop.StartTicks);
    Assert.Throws<InvalidOperationException>(() => loop.InitializeMatch());
    await loop.StopTicksAsync().WaitAsync(TestTimeout);
  }

  [Fact]
  public async Task StartTicks_FailingToStart_LeavesTheLoopStoppedSoStopDoesNotHang()
  {
    var (loop, _, recorder) = Create(time: new TimerFailingTimeProvider());

    Assert.Throws<InvalidOperationException>(loop.StartTicks); // GameSession la informa como CriticalError (D-086).
    TickId final = await loop.StopTicksAsync().WaitAsync(TestTimeout);

    Assert.Equal(TickId.Zero, final);
    Assert.False(loop.IsRunning);
    Assert.Empty(recorder.Entries); // No corrió ningún tick: nada que publicar ni avisar desde el loop.
    Assert.Throws<InvalidOperationException>(loop.StartTicks);
  }

  [Fact]
  public async Task EveryTick_IsReportedOnceInOrder_IncludingTheLastOne()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, engine, recorder) = Create(diagnostics: diagnostics);
    engine.EndAt = Tick3;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);

    Assert.Equal([Tick1, Tick2, Tick3], diagnostics.Ticks);
  }

  [Fact]
  public async Task TickDiscardedByAPhaseFailure_IsReportedToo()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, engine, recorder) = Create(diagnostics: diagnostics);
    engine.FailAt = Tick2;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);

    Assert.Equal([Tick1, Tick2], diagnostics.Ticks); // El tick 2 se descartó, pero sus métricas sirven para entender la falla.
  }

  [Fact]
  public async Task TickLongerThanTheInterval_IsReportedAsOverrun()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, engine, recorder) = Create(diagnostics: diagnostics);
    engine.SlowAt = Tick2;
    engine.EndAt = Tick3;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);

    // El motor lo informa en falso; el que decide es la agenda del loop. Los otros ticks no se verifican: con 5 ms de intervalo,
    // un despertar tardío del sistema operativo también puede ser sobrecarga, y la prueba se volvería inestable.
    Assert.True(diagnostics.Metrics.Single(metrics => metrics.Tick == Tick2).IsOverrun);
  }

  [Fact]
  public async Task FailingDiagnostics_AreIgnoredAndTheMatchGoesOn()
  {
    var (loop, engine, recorder) = Create(diagnostics: new ThrowingDiagnostics());
    engine.EndAt = Tick3;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);

    Assert.Equal(["publish 1", "publish 2", "publish 3", "result BothBasesDestroyed"], recorder.Entries);
  }

  [Fact]
  public async Task NothingIsReportedAfterTheStopCompletes()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, _, recorder) = Create(diagnostics: diagnostics);

    loop.StartTicks();
    await recorder.WaitForPublicationsAsync(2).WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);
    int reportedAtStop = diagnostics.Ticks.Count;
    await Task.Delay(SettleTime);

    Assert.Equal(reportedAtStop, diagnostics.Ticks.Count);
  }

  [Fact]
  public async Task TickAbandonedByTheSupervisor_IsNotReportedEvenIfItFinishesLate()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, engine, recorder) = Create(ShortStallTimeout, diagnostics: diagnostics);
    engine.BlockAt = Tick2;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);
    engine.Release.Set(); // El tick 2 termina tarde...
    await Task.Delay(SettleTime);

    Assert.Equal([Tick1], diagnostics.Ticks); // ...y no se informa: la partida ya había terminado.
  }

  [Fact]
  public async Task PhaseFailure_IsReportedWithItsPhaseReasonAndCause_BeforeTheResult()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, engine, recorder) = Create(diagnostics: diagnostics);
    diagnostics.SharedLog = recorder;
    engine.FailAt = Tick2;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);

    var expected = new CriticalFailureReport(
        CriticalFailureKind.PhaseFailed, Tick2, Tick1, SimulationPhase.Movement, "Falla de prueba.", PhaseFailureCause);
    Assert.Equal(expected, Assert.Single(diagnostics.Failures));
    Assert.Equal(["publish 1", "failure PhaseFailed", "result CriticalError"], recorder.Entries); // El motivo, antes del cierre.
  }

  [Fact]
  public async Task ExceptionOutsideThePhases_IsReportedWithTheException()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, engine, recorder) = Create(diagnostics: diagnostics);
    diagnostics.SharedLog = recorder;
    engine.ThrowAt = Tick1;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);

    CriticalFailureReport report = Assert.Single(diagnostics.Failures);
    Assert.Equal((CriticalFailureKind.UnexpectedException, Tick1, TickId.Zero), (report.Kind, report.Tick, report.LastConfirmedTick));
    Assert.Null(report.Phase);
    Assert.IsType<InvalidOperationException>(report.Exception); // Antes se descartaba: ahora queda la traza.
    Assert.Equal(["failure UnexpectedException", "result CriticalError"], recorder.Entries);
  }

  [Fact]
  public async Task PublisherFailure_IsReportedWithTheTickThatCouldNotBePublished()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, _, recorder) = Create(diagnostics: diagnostics);
    diagnostics.SharedLog = recorder;
    recorder.FailToPublishAt = Tick2;

    loop.StartTicks();
    MatchResult result = await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);

    CriticalFailureReport report = Assert.Single(diagnostics.Failures);
    Assert.Equal((CriticalFailureKind.PublisherFailed, Tick2, Tick2), (report.Kind, report.Tick, report.LastConfirmedTick));
    Assert.Same(PublishFailure, report.Exception);
    Assert.Equal(MatchResult.CriticalError(Tick2), result);
    Assert.Equal(["publish 1", "failure PublisherFailed", "result CriticalError"], recorder.Entries);
  }

  [Fact]
  public async Task StalledTick_IsReportedOnceByTheSupervisor_BeforeTheResult()
  {
    var diagnostics = new DiagnosticsRecorder();
    var (loop, engine, recorder) = Create(ShortStallTimeout, diagnostics: diagnostics);
    diagnostics.SharedLog = recorder;
    engine.BlockAt = Tick2;

    loop.StartTicks();
    await recorder.Result.WaitAsync(TestTimeout);
    await loop.StopTicksAsync().WaitAsync(TestTimeout);
    engine.Release.Set(); // El tick bloqueado termina tarde: no genera otro reporte.
    await Task.Delay(SettleTime);

    CriticalFailureReport report = Assert.Single(diagnostics.Failures);
    Assert.Equal((CriticalFailureKind.TickStalled, Tick2, Tick1), (report.Kind, report.Tick, report.LastConfirmedTick));
    Assert.Null(report.Exception);
    Assert.Contains("TickStallTimeout", report.Reason, StringComparison.Ordinal);
    Assert.Equal(["publish 1", "failure TickStalled", "result CriticalError"], recorder.Entries);
  }

  [Fact]
  public async Task FailingDiagnosticsDuringAFailure_StillDeliverTheResult()
  {
    var (loop, engine, recorder) = Create(diagnostics: new ThrowingDiagnostics());
    engine.FailAt = Tick2;

    loop.StartTicks();
    MatchResult result = await recorder.Result.WaitAsync(TestTimeout);

    Assert.Equal(MatchResult.CriticalError(Tick1), result);
  }

  /// <summary>
  /// Observador que guarda, en orden y desde cualquier hilo, lo que recibe. Con <see cref="SharedLog"/>, anota cada falla en el mismo
  /// registro que las publicaciones y los resultados, para verificar el orden entre ellos.
  /// </summary>
  private sealed class DiagnosticsRecorder : ISimulationDiagnostics
  {
    private readonly object _gate = new();
    private readonly List<TickMetrics> _metrics = [];
    private readonly List<CriticalFailureReport> _failures = [];

    public Recorder? SharedLog { get; set; }

    public IReadOnlyList<CriticalFailureReport> Failures
    {
      get
      {
        lock (_gate)
        {
          return [.. _failures];
        }
      }
    }

    public void OnCriticalFailure(CriticalFailureReport report)
    {
      lock (_gate)
      {
        _failures.Add(report);
      }

      SharedLog?.Note($"failure {report.Kind}");
    }

    public IReadOnlyList<TickMetrics> Metrics
    {
      get
      {
        lock (_gate)
        {
          return [.. _metrics];
        }
      }
    }

    public IReadOnlyList<TickId> Ticks => [.. Metrics.Select(metrics => metrics.Tick)];

    public void OnTickCompleted(TickMetrics metrics)
    {
      lock (_gate)
      {
        _metrics.Add(metrics);
      }
    }
  }

  /// <summary>Observador roto: lanza en cada aviso.</summary>
  private sealed class ThrowingDiagnostics : ISimulationDiagnostics
  {
    public void OnTickCompleted(TickMetrics metrics) => throw new InvalidOperationException("Falla al loguear (prueba).");

    public void OnCriticalFailure(CriticalFailureReport report) => throw new InvalidOperationException("Falla al loguear (prueba).");
  }

  /// <summary>Reloj real que no puede crear temporizadores: simula que falla el arranque del supervisor.</summary>
  private sealed class TimerFailingTimeProvider : TimeProvider
  {
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        throw new InvalidOperationException("Temporizador no disponible (prueba).");
  }

  /// <summary>
  /// Motor falso: cada tick avanza el número de tick y publica vacío. Se le puede indicar en qué tick terminar la partida,
  /// fallar, lanzar una excepción o bloquearse hasta que la prueba lo libere. Detecta si dos ticks se solapan.
  /// </summary>
  private sealed class ScriptedEngine : ISimulationEngine
  {
    private int _ticksInside;

    public TickId? EndAt { get; set; }

    public TickId? FailAt { get; set; }

    public TickId? ThrowAt { get; set; }

    public TickId? BlockAt { get; set; }

    /// <summary>Tick que tarda <see cref="SlowTickDuration"/>, bastante más que el intervalo de prueba (5 ms).</summary>
    public TickId? SlowAt { get; set; }

    public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ManualResetEventSlim Release { get; } = new(initialState: false);

    public bool Overlapped { get; private set; }

    public WorldState? Current { get; private set; }

    public WorldView InitializeMatch()
    {
      Current = TestWorld.Create();
      return WorldViewProjector.Project(Current);
    }

    public TickOutcome AdvanceTick()
    {
      if (Interlocked.Increment(ref _ticksInside) > 1)
      {
        Overlapped = true;
      }

      try
      {
        return NextOutcome(Current!.Tick.Next());
      }
      finally
      {
        Interlocked.Decrement(ref _ticksInside);
      }
    }

    private TickOutcome NextOutcome(TickId tick)
    {
      if (tick == BlockAt)
      {
        Blocked.TrySetResult();
        Release.Wait(TestTimeout);
      }

      if (tick == SlowAt)
      {
        Thread.Sleep(SlowTickDuration);
      }

      if (tick == ThrowAt)
      {
        throw new InvalidOperationException("Excepción de prueba fuera de las fases.");
      }

      if (tick == FailAt)
      {
        var failure = new SimulationFailure(SimulationPhase.Movement, "Falla de prueba.", PhaseFailureCause);
        return new TickOutcome(null, null, failure, Metrics(tick));
      }

      MatchResult? result = tick == EndAt ? MatchResult.Draw(tick) : null;
      Current = Current!.With(tick: tick);
      var publication = new TickPublication(tick, Current.MapRevision, default, default, default, result);
      return new TickOutcome(publication, result, null, Metrics(tick));
    }

    private static TickMetrics Metrics(TickId tick) =>
        new(tick, TimeSpan.Zero, default, TimeSpan.Zero, 0, 0, 0, 0, 0, IsOverrun: false);
  }

  /// <summary>Hace de State Sync y de GameSession: anota, en orden y desde cualquier hilo, lo que le entrega el loop.</summary>
  private sealed class Recorder : ITickPublisher, IMatchResultSink
  {
    private readonly object _gate = new();
    private readonly List<string> _entries = [];
    private readonly SemaphoreSlim _published = new(0);
    private readonly TaskCompletionSource<MatchResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<MatchResult> Result => _result.Task;

    public IReadOnlyList<string> Entries
    {
      get
      {
        lock (_gate)
        {
          return [.. _entries];
        }
      }
    }

    /// <summary>Si no es nulo, <see cref="Publish"/> lanza al recibir ese tick (simula un State Sync roto).</summary>
    public TickId? FailToPublishAt { get; set; }

    public void Publish(TickPublication publication)
    {
      if (publication.Tick == FailToPublishAt)
      {
        throw PublishFailure;
      }

      Add($"publish {publication.Tick.Value}");
      _published.Release();
    }

    /// <summary>Anota algo de otro participante (el observador) en el mismo registro, para verificar el orden.</summary>
    public void Note(string entry) => Add(entry);

    public void OnSimulationResult(MatchResult result)
    {
      Add($"result {result.Reason}");
      _result.TrySetResult(result);
    }

    public async Task WaitForPublicationsAsync(int count)
    {
      for (int i = 0; i < count; i++)
      {
        await _published.WaitAsync();
      }
    }

    private void Add(string entry)
    {
      lock (_gate)
      {
        _entries.Add(entry);
      }
    }
  }
}
