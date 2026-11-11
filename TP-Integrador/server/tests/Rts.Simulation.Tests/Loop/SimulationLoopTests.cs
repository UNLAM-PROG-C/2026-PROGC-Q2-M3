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
  private static readonly TickRate FastRate = new(FastTicksPerSecond);
  private static readonly TickId Tick1 = TickId.Zero.Next();
  private static readonly TickId Tick2 = Tick1.Next();
  private static readonly TickId Tick3 = Tick2.Next();

  private static (SimulationLoop Loop, ScriptedEngine Engine, Recorder Recorder) Create(TimeSpan? stallTimeout = null)
  {
    var engine = new ScriptedEngine();
    var recorder = new Recorder();
    var loop = new SimulationLoop(engine, recorder, recorder, FastRate, stallTimeout ?? GenerousStallTimeout, TimeProvider.System);
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
        new SimulationLoop(new ScriptedEngine(), recorder, recorder, FastRate, FastRate.TickInterval, TimeProvider.System));
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
    var uninitialized = new SimulationLoop(new ScriptedEngine(), recorder, recorder, FastRate, GenerousStallTimeout, TimeProvider.System);
    Assert.Throws<InvalidOperationException>(uninitialized.StartTicks);

    var (loop, _, _) = Create();
    loop.StartTicks();
    Assert.Throws<InvalidOperationException>(loop.StartTicks);
    Assert.Throws<InvalidOperationException>(() => loop.InitializeMatch());
    await loop.StopTicksAsync().WaitAsync(TestTimeout);
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

      if (tick == ThrowAt)
      {
        throw new InvalidOperationException("Excepción de prueba fuera de las fases.");
      }

      if (tick == FailAt)
      {
        return new TickOutcome(null, null, new SimulationFailure(SimulationPhase.Movement, "Falla de prueba."), Metrics(tick));
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

    public void Publish(TickPublication publication)
    {
      Add($"publish {publication.Tick.Value}");
      _published.Release();
    }

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
