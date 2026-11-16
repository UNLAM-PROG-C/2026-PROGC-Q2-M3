using Rts.Combat.Contracts;
using Rts.Economy.Contracts;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Configuration;
using Rts.Simulation.Engine;
using Rts.Simulation.Phases;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Engine;

/// <summary>
/// Casos de 06 §11 (y AGENTS §7) en los que varias cosas pasan en el <b>mismo tick</b>. Los tres primeros corren las fases 4 a 8 con fakes
/// de Economy, Combat y Production; el último, el motor completo.
/// </summary>
public class SameTickCasesTests
{
  private const int FoodPerBatch = 4;
  private const int BarracksHp = 600;

  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
  private static readonly GridCell FarmAnchor = new(28, 28);
  private static readonly GridCell BarracksAnchor = new(12, 12);
  private static readonly FootprintSize BarracksSize = new(3, 3);
  private static readonly PositionXZ Battlefield = new(20.5, 20.5);
  private static readonly PositionXZ NextToBattlefield = new(21.5, 20.5);
  private static readonly GridCell FreeAnchor = new(25, 10);
  private static readonly PositionXZ NextToFreeAnchor = new(24.5, 10.5);

  /// <summary>Fases 4 a 8: snapshot, Economy ∥ Combat, barrera y commit, Victory Rules y Production.</summary>
  private static void RunPhasesFourToEight(
      TickContext context, FakeEconomyCalculator economy, FakeCombatCalculator combat, FakeProductionCalculator production)
  {
    var economyAdapter = new EconomyAdapter(economy, new FakeMovementRules());
    var combatAdapter = new CombatAdapter(combat);
    ITickPhase[] phases =
    [
      new SnapshotPhase(),
      new EconomyCombatLaunchPhase(economyAdapter, combatAdapter, runInParallel: true),
      new BarrierAndCommitPhase(economyAdapter, combatAdapter, TestTimeout, TimeProvider.System),
      new VictoryRulesPhase(),
      new ProductionPhase(production, new FakeEconomyRules(), new FakeCombatRules()),
    ];
    foreach (ITickPhase phase in phases)
    {
      phase.Execute(context);
    }
  }

  private static FakeCombatCalculator Destroying(params (EntityId Target, int Hp)[] targets) => new()
  {
    Respond = input => CombatTickResult.Empty(input.Tick) with
    {
      Damage = [.. targets.Select(target => new TargetDamage(target.Target, target.Hp, target.Hp))],
      ProposedEvents = [.. targets.Select(target => (IDomainEventPayload)new EntityDestroyed(target.Target))],
    },
  };

  [Fact]
  public void FarmThatProducesAndIsDestroyedInTheSameTick_StillCreditsTheFood()
  {
    (WorldState world, EntityId farmId) = TestWorld.WithFarm(TestWorld.Create(), PlayerId.One, FarmAnchor);
    ResourceAmount before = world.Players[PlayerId.One].Resources;
    var economy = new FakeEconomyCalculator
    {
      Respond = input => EconomyTickResult.Empty(input.Tick) with
      {
        PlayerDeltas = [new PlayerResourceDelta(PlayerId.One, ResourceAmount.OfFood(FoodPerBatch))],
        FoodProduced = [new FoodProduced(farmId, PlayerId.One, FoodPerBatch)],
      },
    };
    var context = new TickContext(world);

    RunPhasesFourToEight(context, economy, Destroying((farmId, TestWorld.FarmHp)), new FakeProductionCalculator());

    Assert.False(context.World.Buildings.ContainsKey(farmId));
    Assert.Equal(before + ResourceAmount.OfFood(FoodPerBatch), context.World.Players[PlayerId.One].Resources); // "Se acredita y después se destruye".
    Assert.Equal(world.MapRevision.Next(), context.World.MapRevision); // La huella quedó libre.
  }

  [Fact]
  public void MutualAttacks_BothSoldiersDieInTheSameTickWithoutCascadeEvents()
  {
    WorldState world = TestWorld.Create();
    EntityId p2SoldierId = world.LastEntityId.Next().Next(); // El segundo soldado que se va a crear.
    (world, EntityId p1Soldier) = TestWorld.WithSoldier(world, PlayerId.One, Battlefield, attackTarget: p2SoldierId);
    (world, EntityId p2Soldier) = TestWorld.WithSoldier(world, PlayerId.Two, NextToBattlefield, attackTarget: p1Soldier);
    Assert.Equal(p2SoldierId, p2Soldier);
    var context = new TickContext(world);

    RunPhasesFourToEight(
        context,
        new FakeEconomyCalculator(),
        Destroying((p1Soldier, FakeCombatRules.SoldierMaxHp), (p2Soldier, FakeCombatRules.SoldierMaxHp)),
        new FakeProductionCalculator());

    Assert.False(context.World.Units.ContainsKey(p1Soldier));
    Assert.False(context.World.Units.ContainsKey(p2Soldier));
    Assert.Equal(
        [new EntityDestroyed(p1Soldier), new EntityDestroyed(p2Soldier)],
        context.Events.Select(stamped => stamped.Payload)); // Ningún "pasó a Idle" sobre unidades que ya no existen.
    Assert.Null(context.World.GameplayResult); // Murieron soldados, no bases: la partida sigue.
  }

  [Fact]
  public void ProducerDestroyedInPhaseSix_IsNotOfferedToProductionAndStartsNothing()
  {
    (WorldState world, EntityId barracks) = WithBarracksWaitingToTrain(TestWorld.Create());
    var production = new FakeProductionCalculator();
    var context = new TickContext(world);

    RunPhasesFourToEight(context, new FakeEconomyCalculator(), Destroying((barracks, BarracksHp)), production);

    ProductionTickInput input = Assert.Single(production.Inputs);
    Assert.DoesNotContain(input.Producers, producer => producer.ProducerId == barracks);
    Assert.Contains(input.Producers, producer => producer.ProducerId == TestWorld.P1Center); // Los demás productores sí.
    Assert.Equal(world.Players[PlayerId.One].Resources, context.World.Players[PlayerId.One].Resources); // Nadie cobró comida.
  }

  [Fact]
  public void DuplicatePayingCommand_InTheSameBatchAndResentLater_PaysOnceAndPlacesOneSite()
  {
    SimulationConfig config = ConfigAndMapTests.ValidConfig();
    var queue = new CommandQueue(config.CommandQueueCapacity);
    ISimulationEngine engine = SimulationFactory.CreateEngine(
        config, TestWorld.Map, TestWorld.Economy, queue, TestModules.Create(), TimeProvider.System);
    ResourceAmount before = engine.InitializeMatch().Players.Single(player => player.Player == PlayerId.One).Resources;
    var commandId = new ClientCommandId(1);
    BuildCommand build = BuildCommand.Place(
        BuildingType.Farm, FreeAnchor, MapRevision.Initial, [new UnitRouteCandidate(TestWorld.P1WorkerA, [NextToFreeAnchor])]);

    queue.Admit(PlayerId.One, commandId, build);
    queue.Admit(PlayerId.One, commandId, build); // Duplicado en el mismo lote.
    TickOutcome first = engine.AdvanceTick();
    queue.Admit(PlayerId.One, commandId, build); // Reenvío en el tick siguiente.
    TickOutcome second = engine.AdvanceTick();

    CommandResult[] results = [.. first.Publication!.CommandResults, .. second.Publication!.CommandResults];
    Assert.Equal(3, results.Length);
    Assert.All(results, result => Assert.Equal((CommandOutcome.Accepted, first.Publication.Tick), (result.Outcome, result.Tick)));
    Assert.Equal(before - FakeEconomyRules.FarmCost, engine.Current!.Players[PlayerId.One].Resources);
    Assert.Single(engine.Current.Buildings.Values, building => building.Type == BuildingType.Farm);
  }

  /// <summary>Un Cuartel completado de P1 con un soldado en cola, esperando comida.</summary>
  private static (WorldState World, EntityId BarracksId) WithBarracksWaitingToTrain(WorldState world)
  {
    EntityId id = world.LastEntityId.Next();
    var footprint = new Footprint(BarracksAnchor, BarracksSize);
    var queued = new ProductionRequest(CommandSequence.First, id, PlayerId.One, UnitType.Soldier);
    var producer = new ProducerState(id, PlayerId.One, BuildingType.Barracks, footprint, TickId.Zero, activeJob: null, [queued]);
    var barracks = new BuildingEntity(
        id, PlayerId.One, BuildingType.Barracks, footprint, BarracksHp, TickId.Zero,
        farmEconomy: null, repair: null, producer, towerReadyAtTick: null);
    return (world.With(buildings: world.Buildings.SetItem(id, barracks), lastEntityId: id), id);
  }
}
