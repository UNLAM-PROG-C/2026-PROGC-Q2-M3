using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Resources;
using Rts.Shared.Time;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.Initialization;
using Rts.Simulation.Map;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;

namespace Rts.Simulation.Tests.Engine;

public class SimulationEngineTests
{
  private const int MaxWoodPerTree = 100;
  private static readonly ResourceAmount WoodGain = new(Wood: 5, Food: 0);
  private static readonly EntityId DamagedEntity = new(1);
  private const int HpAfterDamage = 10;

  private static readonly MapDefinition Map = new(
      new GridSpec(1.0, 40, 40),
      [new GridCell(10, 20)],
      [
        new PlayerStartDefinition(PlayerId.One, new GridCell(2, 2), [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(7.5, 3.5))]),
        new PlayerStartDefinition(PlayerId.Two, new GridCell(34, 34), [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(32.5, 36.5))]),
      ]);

  private static SimulationEngine CreateEngine(params ITickPhase[] phases)
  {
    var factory = new InitialWorldFactory(Map, MaxWoodPerTree, new FakeEconomyRules(), new FakeConstructionRules(), new FakeCombatRules());
    return new SimulationEngine(factory, phases, TickRate.Default, TimeProvider.System);
  }

  private static SimulationEngine CreateInitializedEngine(params ITickPhase[] phases)
  {
    SimulationEngine engine = CreateEngine(phases);
    engine.InitializeMatch();
    return engine;
  }

  private static ITickPhase Phase(SimulationPhase phase, Action<TickContext> action) => new DelegatePhase(phase, action);

  private static ITickPhase EmitDamage(SimulationPhase phase) =>
      Phase(phase, context => context.Emit(new EntityDamaged(DamagedEntity, HpAfterDamage)));

  private static ITickPhase GiveWoodToP1() => Phase(SimulationPhase.BarrierAndCommit, context =>
  {
    PlayerState p1 = context.World.Players[PlayerId.One];
    var players = context.World.Players.SetItem(PlayerId.One, new PlayerState(PlayerId.One, p1.Resources + WoodGain));
    context.World = context.World.With(players: players);
  });

  [Fact]
  public void AdvanceTick_BeforeInitializeMatch_Throws()
  {
    Assert.Throws<InvalidOperationException>(() => CreateEngine().AdvanceTick());
  }

  [Fact]
  public void InitializeMatch_SetsCurrentAtTickZeroAndReturnsItsView()
  {
    SimulationEngine engine = CreateEngine();

    var view = engine.InitializeMatch();

    Assert.NotNull(engine.Current);
    Assert.Equal(TickId.Zero, engine.Current.Tick);
    Assert.Equal(WorldViewProjector.Project(engine.Current), view);
  }

  [Fact]
  public void AdvanceTick_EmptyPipeline_ConfirmsNextTickWithAllPositions()
  {
    SimulationEngine engine = CreateInitializedEngine();
    WorldState before = engine.Current!;

    TickOutcome outcome = engine.AdvanceTick();

    Assert.Null(outcome.Failure);
    Assert.NotNull(outcome.Publication);
    Assert.Equal(TickId.Zero.Next(), outcome.Publication.Tick);
    Assert.Equal(TickId.Zero.Next(), engine.Current!.Tick);
    Assert.Empty(outcome.Publication.Events);
    Assert.Equal(before.Units.Keys, outcome.Publication.Positions.Select(p => p.UnitId));
  }

  [Fact]
  public void AdvanceTick_RunsPhasesInOrderAndStampsEventsConsecutively()
  {
    var executed = new List<SimulationPhase>();
    SimulationEngine engine = CreateInitializedEngine(
        Phase(SimulationPhase.CommandBatch, _ => executed.Add(SimulationPhase.CommandBatch)),
        EmitDamage(SimulationPhase.Movement),
        Phase(SimulationPhase.Construction, _ => executed.Add(SimulationPhase.Construction)),
        EmitDamage(SimulationPhase.BarrierAndCommit));

    TickOutcome outcome = engine.AdvanceTick();

    Assert.Equal([SimulationPhase.CommandBatch, SimulationPhase.Construction], executed);
    TickId tick = TickId.Zero.Next();
    Assert.Equal([new EventStamp(tick, 0), new EventStamp(tick, 1)], outcome.Publication!.Events.Select(e => e.Stamp));
  }

  [Fact]
  public void AdvanceTick_BalanceChange_EmitsResourcesChangedLastAndOnlyForThatPlayer()
  {
    SimulationEngine engine = CreateInitializedEngine(GiveWoodToP1(), EmitDamage(SimulationPhase.Production));

    TickOutcome outcome = engine.AdvanceTick();

    DomainEvent last = outcome.Publication!.Events[^1];
    var expected = new ResourcesChanged(PlayerId.One, FakeEconomyRules.TestInitialBalance + WoodGain);
    Assert.Equal(expected, last.Payload);
    Assert.Single(outcome.Publication.Events, e => e.Payload is ResourcesChanged);
  }

  [Fact]
  public void AdvanceTick_PhaseThrows_DiscardsWholeTickAndHalts()
  {
    SimulationEngine engine = CreateInitializedEngine(
        GiveWoodToP1(),
        Phase(SimulationPhase.Production, _ => throw new InvalidOperationException("falla de prueba")));
    WorldState before = engine.Current!;

    TickOutcome outcome = engine.AdvanceTick();

    Assert.Null(outcome.Publication);
    Assert.NotNull(outcome.Failure);
    Assert.Equal(SimulationPhase.Production, outcome.Failure.Phase);
    Assert.Same(before, engine.Current); // El cambio de saldo de la fase anterior tampoco quedó.
    Assert.Throws<InvalidOperationException>(() => engine.AdvanceTick());
  }

  [Fact]
  public void AdvanceTick_GameplayResult_IsReportedOnceAndHalts()
  {
    SimulationEngine engine = CreateInitializedEngine(Phase(SimulationPhase.VictoryRules, context =>
        context.World = context.World.With(gameplayResult: MatchResult.BaseDestroyed(PlayerId.One, context.Tick))));

    TickOutcome outcome = engine.AdvanceTick();

    Assert.Equal(MatchResult.BaseDestroyed(PlayerId.One, TickId.Zero.Next()), outcome.GameplayResult);
    Assert.Equal(outcome.GameplayResult, outcome.Publication!.GameplayResult);
    Assert.Throws<InvalidOperationException>(() => engine.AdvanceTick());
  }

  [Fact]
  public void InitializeMatch_AfterHalt_AllowsTicksAgain()
  {
    SimulationEngine engine = CreateInitializedEngine(Phase(SimulationPhase.Movement, _ => throw new InvalidOperationException()));
    engine.AdvanceTick();

    engine.InitializeMatch();

    Assert.Equal(TickId.Zero, engine.Current!.Tick);
  }

  [Fact]
  public void AdvanceTick_MetricsListEveryPhaseAndPublication()
  {
    SimulationEngine engine = CreateInitializedEngine(EmitDamage(SimulationPhase.CommandBatch), EmitDamage(SimulationPhase.Movement));

    TickMetrics metrics = engine.AdvanceTick().Metrics;

    Assert.Equal(TickId.Zero.Next(), metrics.Tick);
    Assert.Equal(
        [SimulationPhase.CommandBatch, SimulationPhase.Movement, SimulationPhase.Publication],
        metrics.Phases.Select(p => p.Phase));
    Assert.Equal(2, metrics.EventsPublished);
  }

  [Fact]
  public void AdvanceTick_ManyTicks_AdvanceOneByOne()
  {
    const int TickCount = 5;
    SimulationEngine engine = CreateInitializedEngine();

    for (int i = 0; i < TickCount; i++)
    {
      engine.AdvanceTick();
    }

    Assert.Equal(TickId.Zero.Plus(TickCount), engine.Current!.Tick);
  }

  private sealed class DelegatePhase(SimulationPhase phase, Action<TickContext> action) : ITickPhase
  {
    public SimulationPhase Phase => phase;

    public void Execute(TickContext context) => action(context);
  }
}
