using Rts.Combat.Contracts;
using Rts.Economy.Contracts;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Time;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.Phases;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Phases;

public class EconomyCombatPhasesTests
{
  private const int BranchCount = 2;
  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan ShortBarrier = TimeSpan.FromMilliseconds(100);

  private static ITickPhase[] Phases(FakeBranch<EconomyTickResult> economy, FakeBranch<CombatTickResult> combat, bool parallel, TimeSpan? barrier = null) =>
  [
    new SnapshotPhase(),
    new EconomyCombatLaunchPhase(economy, combat, parallel),
    new BarrierAndCommitPhase(economy, combat, barrier ?? TestTimeout, TimeProvider.System),
  ];

  private static void Run(TickContext context, IEnumerable<ITickPhase> phases)
  {
    foreach (ITickPhase phase in phases)
    {
      phase.Execute(context);
    }
  }

  private static (FakeBranch<EconomyTickResult> Economy, FakeBranch<CombatTickResult> Combat, List<string> Log) Branches()
  {
    var log = new List<string>();
    return (new FakeBranch<EconomyTickResult>("economy", EconomyTickResult.Empty, log), new FakeBranch<CombatTickResult>("combat", CombatTickResult.Empty, log), log);
  }

  [Fact]
  public void Parallel_BothBranchesAreCalculatingAtTheSameTime()
  {
    var (economy, combat, _) = Branches();
    using var meeting = new Barrier(BranchCount);
    bool economyMet = false;
    bool combatMet = false;
    economy.OnCalculate = () => economyMet = meeting.SignalAndWait(TestTimeout);
    combat.OnCalculate = () => combatMet = meeting.SignalAndWait(TestTimeout);

    Run(new TickContext(TestWorld.Create()), Phases(economy, combat, parallel: true));

    Assert.True(economyMet && combatMet); // Solo pueden encontrarse si corren a la vez.
  }

  [Fact]
  public void Sequential_RunsBothOnTheSimulationThreadEconomyFirst()
  {
    var (economy, combat, log) = Branches();
    int simulationThread = Environment.CurrentManagedThreadId;

    Run(new TickContext(TestWorld.Create()), Phases(economy, combat, parallel: false));

    Assert.Equal([simulationThread, simulationThread], [economy.CalculatedOnThread, combat.CalculatedOnThread]);
    Assert.Equal(["calculate economy", "calculate combat", "commit economy", "commit combat"], log);
  }

  [Fact]
  public void Parallel_CombatFinishingFirst_StillCommitsEconomyFirst()
  {
    var (economy, combat, log) = Branches();
    using var combatDone = new ManualResetEventSlim();
    economy.OnCalculate = () => combatDone.Wait(TestTimeout); // Economy termina después de Combat, a propósito.
    combat.OnFinished = combatDone.Set;

    Run(new TickContext(TestWorld.Create()), Phases(economy, combat, parallel: true));

    Assert.Equal(["calculate combat", "calculate economy", "commit economy", "commit combat"], log);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void BothBranchesReadTheSameSnapshot(bool parallel)
  {
    var (economy, combat, _) = Branches();
    var context = new TickContext(TestWorld.Create());

    Run(context, Phases(economy, combat, parallel));

    Assert.Same(context.Snapshot, economy.SnapshotSeen);
    Assert.Same(context.Snapshot, combat.SnapshotSeen);
    Assert.Equal(context.Tick, economy.TickSeen);
  }

  [Fact]
  public void BranchThatFails_FailsTheBarrierNamingItAndCommitsNothing()
  {
    var (economy, combat, log) = Branches();
    combat.OnCalculate = () => throw new InvalidOperationException("error de prueba");

    var error = Assert.Throws<InvalidOperationException>(() => Run(new TickContext(TestWorld.Create()), Phases(economy, combat, parallel: true)));

    Assert.StartsWith("Combat falló", error.Message, StringComparison.Ordinal);
    Assert.DoesNotContain(log, entry => entry.StartsWith("commit", StringComparison.Ordinal));
  }

  [Fact]
  public void BranchThatHangs_ExceedsTheBarrierAndRecordsTheWait()
  {
    var (economy, combat, log) = Branches();
    using var release = new ManualResetEventSlim();
    economy.OnCalculate = () => release.Wait(TestTimeout);
    var context = new TickContext(TestWorld.Create());

    Assert.Throws<TimeoutException>(() => Run(context, Phases(economy, combat, parallel: true, ShortBarrier)));
    release.Set();

    Assert.True(context.BarrierWait >= ShortBarrier);
    Assert.DoesNotContain(log, entry => entry.StartsWith("commit", StringComparison.Ordinal));
  }

  [Fact]
  public void Cascade_EmitsCanceledOrdersByUnitIdAndSkipsUnitsThatDiedInTheSameTick()
  {
    var (economy, combat, _) = Branches();
    economy.OnCommit = context =>
    {
      context.CanceledOrders.Add(TestWorld.P1WorkerB);
      context.CanceledOrders.Add(TestWorld.P1WorkerA);
    };
    combat.OnCommit = context => context.World = context.World.With(units: context.World.Units.Remove(TestWorld.P1WorkerA)); // Murió.
    var context = new TickContext(TestWorld.Create());

    Run(context, Phases(economy, combat, parallel: true));

    Assert.Equal(
        [new UnitOrderChanged(TestWorld.P1WorkerB, OrderId.None, UnitOrderKind.Idle)],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void ThroughTheEngine_CombatFailureDiscardsTheWholeTickIncludingEconomysCommit()
  {
    var (economy, combat, _) = Branches();
    economy.OnCommit = context => context.World = context.World.With(lastEntityId: context.World.LastEntityId.Next());
    combat.OnCommit = _ => throw new InvalidOperationException("Combat incoherente.");
    var engine = new SimulationEngine(TestWorld.Factory(), Phases(economy, combat, parallel: true), TickRate.Default, TimeProvider.System);
    engine.InitializeMatch();
    WorldState before = engine.Current!;

    TickOutcome outcome = engine.AdvanceTick();

    Assert.Equal(SimulationPhase.BarrierAndCommit, outcome.Failure!.Phase);
    Assert.Same(before, engine.Current); // Economy ya había confirmado sobre W, pero W se descarta entero (D-044).
    Assert.Contains(outcome.Metrics.Phases, timing => timing.Phase == SimulationPhase.Snapshot);
  }

  /// <summary>
  /// Rama de prueba: anota en orden (bajo un lock, porque escriben dos hilos) cuándo calcula y cuándo confirma, y permite inyectar
  /// esperas, errores o cambios sobre el mundo.
  /// </summary>
  private sealed class FakeBranch<TResult>(string name, Func<TickId, TResult> empty, List<string> log) : IDomainBranch<TResult>
      where TResult : class
  {
    public Action? OnCalculate { get; set; }

    public Action? OnFinished { get; set; }

    public Action<TickContext>? OnCommit { get; set; }

    public WorldState? SnapshotSeen { get; private set; }

    public TickId? TickSeen { get; private set; }

    public int CalculatedOnThread { get; private set; }

    public TResult Calculate(WorldState snapshot, TickId tick)
    {
      SnapshotSeen = snapshot;
      TickSeen = tick;
      CalculatedOnThread = Environment.CurrentManagedThreadId;
      OnCalculate?.Invoke();
      Add($"calculate {name}");
      OnFinished?.Invoke();
      return empty(tick);
    }

    public void Commit(TickContext context, TResult result)
    {
      Add($"commit {name}");
      OnCommit?.Invoke(context);
    }

    private void Add(string entry)
    {
      lock (log)
      {
        log.Add(entry);
      }
    }
  }
}
