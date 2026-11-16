using Rts.Combat.Contracts;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Simulation.Engine;
using Rts.Simulation.Phases;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Phases;

public class EconomyAdapterTests
{
  private const int GatherTicksSoFar = 7;
  private const int WoodPerGather = 5;
  private const int WoodLeft = TestWorld.MaxWoodPerTree - WoodPerGather;
  private static readonly GridCell CompletedFarmAnchor = new(20, 20);
  private static readonly GridCell FarmSiteAnchor = new(24, 24);

  /// <summary>
  /// P1: el trabajador A recolecta parado al lado del árbol (en rango); el B tiene la misma orden pero sigue lejos, caminando.
  /// Además, una granja completada de P1 y otra en obra.
  /// </summary>
  private static (WorldState World, EntityId Farm) GatheringWorld()
  {
    WorldState world = TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, GatherTicksSoFar);
    world = TestWorld.WithUnitAt(world, TestWorld.P1WorkerA, TestWorld.NextToTree);
    world = TestWorld.WithGatheringUnit(world, TestWorld.P1WorkerB, gatherTicks: 0);
    (world, EntityId farm) = TestWorld.WithFarm(world, PlayerId.One, CompletedFarmAnchor);
    (world, _) = TestWorld.WithFarm(world, PlayerId.One, FarmSiteAnchor, completed: false);
    return (world, farm);
  }

  private static EconomyTickResult Result(
      TickId tick,
      PlayerResourceDelta[]? deltas = null,
      GathererState[]? gatherers = null,
      EntityId[]? toIdle = null,
      TreeEconomyState[]? trees = null,
      FarmEconomyState[]? farms = null,
      IDomainEventPayload[]? events = null) =>
      new(tick, [.. deltas ?? []], default, default, [.. gatherers ?? []], [.. toIdle ?? []], [.. trees ?? []], [.. farms ?? []], [.. events ?? []]);

  [Fact]
  public void Calculate_SendsOnlyGatherersInRangeEveryTreeAndOnlyCompletedFarms()
  {
    var (world, farm) = GatheringWorld();
    var calculator = new FakeEconomyCalculator();
    TickId tick = world.Tick.Next();

    new EconomyAdapter(calculator, new FakeMovementRules()).Calculate(world, tick);

    EconomyTickInput input = Assert.Single(calculator.Inputs);
    Assert.Equal(tick, input.Tick);
    Assert.Equal([new GathererState(TestWorld.P1WorkerA, PlayerId.One, TestWorld.Tree, GatherTicksSoFar)], input.Gatherers);
    Assert.Equal([TestWorld.TreeState(world)], input.Trees);
    Assert.Equal([world.Buildings[farm].FarmEconomy!], input.Farms);
  }

  [Fact]
  public void Commit_AppliesCreditsTreesFarmsAndGatherCounters()
  {
    var (world, farm) = GatheringWorld();
    var context = new TickContext(world);
    var farmAfter = new FarmEconomyState(farm, PlayerId.One, FarmProductionStatus.Producing, TestWorld.FarmBatches - 1, 0, TickId.Zero);
    EconomyTickResult result = Result(
        context.Tick,
        deltas: [new PlayerResourceDelta(PlayerId.One, ResourceAmount.OfWood(WoodPerGather))],
        gatherers: [new GathererState(TestWorld.P1WorkerA, PlayerId.One, TestWorld.Tree, 0)],
        trees: [TreeEconomyState.Available(TestWorld.Tree, WoodLeft)],
        farms: [farmAfter]);

    new EconomyAdapter(new FakeEconomyCalculator(), new FakeMovementRules()).Commit(context, result);

    WorldState after = context.World;
    Assert.Equal(FakeEconomyRules.TestInitialBalance + ResourceAmount.OfWood(WoodPerGather), after.Players[PlayerId.One].Resources);
    Assert.Equal(FakeEconomyRules.TestInitialBalance, after.Players[PlayerId.Two].Resources);
    Assert.Equal(WoodLeft, TestWorld.TreeState(after).WoodRemaining);
    Assert.Equal(farmAfter, after.Buildings[farm].FarmEconomy);
    Assert.Equal(0, after.Units[TestWorld.P1WorkerA].GatherTicksAccumulated);
    Assert.Equal(UnitOrderKind.GatherWood, after.Units[TestWorld.P1WorkerA].Order.Kind);
  }

  [Fact]
  public void DepletedTree_IdlesEveryGathererIncludingWalkersAndTheCascadeEmitsAfterEconomysEvents()
  {
    var (world, _) = GatheringWorld();
    var calculator = new FakeEconomyCalculator
    {
      Respond = input => Result(
          input.Tick,
          toIdle: [TestWorld.P1WorkerA], // Economy solo conoce al que está en rango.
          trees: [TreeEconomyState.Depleted(TestWorld.Tree, input.Tick)],
          events: [new TreeDepleted(TestWorld.Tree)]),
    };
    var context = new TickContext(world);

    RunEconomyCombatPhases(context, new EconomyAdapter(calculator, new FakeMovementRules()));

    Assert.True(context.World.Units[TestWorld.P1WorkerA].Order.IsIdle);
    Assert.True(context.World.Units[TestWorld.P1WorkerB].Order.IsIdle); // También el que todavía caminaba.
    Assert.Equal(
        [
          new TreeDepleted(TestWorld.Tree),
          new UnitOrderChanged(TestWorld.P1WorkerA, OrderId.None, UnitOrderKind.Idle),
          new UnitOrderChanged(TestWorld.P1WorkerB, OrderId.None, UnitOrderKind.Idle),
        ],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void Commit_IncoherentResult_Throws()
  {
    var (world, _) = GatheringWorld();
    var branch = new EconomyAdapter(new FakeEconomyCalculator(), new FakeMovementRules());
    var wrongTick = new TickContext(world);
    var unknownTree = new TickContext(world);

    Assert.Throws<InvalidOperationException>(() => branch.Commit(wrongTick, Result(wrongTick.Tick.Next())));
    Assert.Throws<InvalidOperationException>(() =>
        branch.Commit(unknownTree, Result(unknownTree.Tick, trees: [TreeEconomyState.Available(TestWorld.Missing, WoodLeft)])));
  }

  /// <summary>Fases 4, 5 y 6 con la rama Economy real y una rama Combat que no hace nada.</summary>
  private static void RunEconomyCombatPhases(TickContext context, EconomyAdapter economy)
  {
    var combat = new NoCombat();
    ITickPhase[] phases =
    [
      new SnapshotPhase(),
      new EconomyCombatLaunchPhase(economy, combat, runInParallel: true),
      new BarrierAndCommitPhase(economy, combat, TimeSpan.FromSeconds(5), TimeProvider.System),
    ];
    foreach (ITickPhase phase in phases)
    {
      phase.Execute(context);
    }
  }

  private sealed class NoCombat : IDomainBranch<CombatTickResult>
  {
    public CombatTickResult Calculate(WorldState snapshot, TickId tick) => CombatTickResult.Empty(tick);

    public void Commit(TickContext context, CombatTickResult result)
    {
    }
  }
}
