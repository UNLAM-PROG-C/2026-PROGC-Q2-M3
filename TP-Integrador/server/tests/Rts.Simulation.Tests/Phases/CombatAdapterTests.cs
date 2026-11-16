using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Economy.Contracts;
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

public class CombatAdapterTests
{
  private const int SomeDamage = 10;
  private const int WoodPerGather = 5;
  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
  private static readonly PositionXZ NearP2Worker = new(31.5, 36.5);
  private static readonly GridCell P2TowerAnchor = new(30, 30);
  private static readonly GridCell P1TowerSiteAnchor = new(12, 12);
  private static readonly GridCell P2FarmAnchor = new(28, 28);

  /// <summary>P1: un soldado atacando al trabajador de P2 y otro quieto, y una torre en obra. P2: una torre completada.</summary>
  private static (WorldState World, EntityId Attacker, EntityId IdleSoldier, EntityId Tower) CombatWorld()
  {
    WorldState world = TestWorld.Create();
    (world, EntityId attacker) = TestWorld.WithSoldier(world, PlayerId.One, NearP2Worker, attackTarget: TestWorld.P2Worker);
    (world, EntityId idle) = TestWorld.WithSoldier(world, PlayerId.One, NearP2Worker);
    (world, EntityId tower) = TestWorld.WithTower(world, PlayerId.Two, P2TowerAnchor);
    (world, _) = TestWorld.WithTower(world, PlayerId.One, P1TowerSiteAnchor, completed: false);
    return (world, attacker, idle, tower);
  }

  private static CombatTickResult Result(
      TickId tick,
      TargetDamage[]? damage = null,
      CooldownUpdate[]? cooldowns = null,
      EntityId[]? toIdle = null,
      IDomainEventPayload[]? events = null) =>
      new(tick, default, [.. damage ?? []], [.. cooldowns ?? []], [.. toIdle ?? []], [.. events ?? []]);

  [Fact]
  public void Calculate_SendsEverythingButOnlyAttackingSoldiersAndCompletedTowersAsAttackers()
  {
    var (world, attacker, _, tower) = CombatWorld();
    var calculator = new FakeCombatCalculator();

    new CombatAdapter(calculator).Calculate(world, world.Tick.Next());

    CombatTickInput input = Assert.Single(calculator.Inputs);
    Assert.Equal(world.Units.Keys, input.Units.Select(unit => unit.Id));
    Assert.Equal(world.Buildings.Keys, input.Buildings.Select(building => building.Id));
    Assert.Equal([new SoldierAttacker(attacker, TestWorld.P2Worker, TickId.Zero)], input.Soldiers);
    Assert.Equal([new TowerAttacker(tower, TickId.Zero)], input.Towers);
  }

  [Fact]
  public void Commit_AppliesDamageCooldownsAndSoldiersToIdle()
  {
    var (world, attacker, _, tower) = CombatWorld();
    var context = new TickContext(world);
    TickId readyAgain = context.Tick.Next();
    int p2WorkerHp = world.Units[TestWorld.P2Worker].Hp;

    new CombatAdapter(new FakeCombatCalculator()).Commit(context, Result(
        context.Tick,
        damage: [new TargetDamage(TestWorld.P2Worker, p2WorkerHp, SomeDamage)],
        cooldowns: [new CooldownUpdate(attacker, readyAgain), new CooldownUpdate(tower, readyAgain)],
        toIdle: [attacker]));

    Assert.Equal(p2WorkerHp - SomeDamage, context.World.Units[TestWorld.P2Worker].Hp);
    Assert.Equal(readyAgain, context.World.Units[attacker].ReadyAtTick);
    Assert.Equal(readyAgain, context.World.Buildings[tower].TowerReadyAtTick);
    Assert.True(context.World.Units[attacker].Order.IsIdle);
    Assert.Contains(attacker, context.CanceledOrders);
    Assert.Equal(context.Previous.MapRevision, context.World.MapRevision);
  }

  [Fact]
  public void DestroyedUnit_IsRemovedAndWhoeverTargetedItGoesIdleInTheCascade()
  {
    var (world, attacker, _, _) = CombatWorld();
    int p2WorkerHp = world.Units[TestWorld.P2Worker].Hp;
    var combat = new FakeCombatCalculator
    {
      Respond = input => Result(
          input.Tick,
          damage: [new TargetDamage(TestWorld.P2Worker, p2WorkerHp, p2WorkerHp)],
          events: [new EntityDestroyed(TestWorld.P2Worker)]),
    };
    var context = new TickContext(world);

    RunPhases(context, new FakeEconomyCalculator(), combat);

    Assert.False(context.World.Units.ContainsKey(TestWorld.P2Worker));
    Assert.True(context.World.Units[attacker].Order.IsIdle);
    Assert.Equal(
        [new EntityDestroyed(TestWorld.P2Worker), new UnitOrderChanged(attacker, OrderId.None, UnitOrderKind.Idle)],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void DestroyedBuilding_FreesTheMapCancelsItsBuildersAndAnnouncesTheNewRevisionLast()
  {
    var (world, farm) = TestWorld.WithFarm(TestWorld.Create(), PlayerId.Two, P2FarmAnchor, completed: false);
    world = TestWorld.WithTaskOrder(world, TestWorld.P2Worker, UnitOrderKind.Build, farm);
    var combat = new FakeCombatCalculator
    {
      Respond = input => Result(
          input.Tick,
          damage: [new TargetDamage(farm, TestWorld.FarmHp, TestWorld.FarmHp)],
          events: [new EntityDestroyed(farm)]),
    };
    var context = new TickContext(world);

    RunPhases(context, new FakeEconomyCalculator(), combat);

    MapRevision next = world.MapRevision.Next();
    Assert.False(context.World.Buildings.ContainsKey(farm));
    Assert.Equal(next, context.World.MapRevision);
    Assert.False(context.Navigation.IsBlocked(P2FarmAnchor)); // La huella quedó libre.
    Assert.True(context.World.Units[TestWorld.P2Worker].Order.IsIdle);
    Assert.Equal(
        [new EntityDestroyed(farm), new UnitOrderChanged(TestWorld.P2Worker, OrderId.None, UnitOrderKind.Idle), new MapRevisionChanged(next)],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void WorkerThatGathersAndDiesInTheSameTick_StillCreditsTheWood()
  {
    WorldState world = TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, gatherTicks: 0);
    world = TestWorld.WithUnitAt(world, TestWorld.P1WorkerA, TestWorld.NextToTree);
    int workerHp = world.Units[TestWorld.P1WorkerA].Hp;
    var economy = new FakeEconomyCalculator
    {
      Respond = input => EconomyTickResult.Empty(input.Tick) with
      {
        PlayerDeltas = [new PlayerResourceDelta(PlayerId.One, ResourceAmount.OfWood(WoodPerGather))],
      },
    };
    var combat = new FakeCombatCalculator
    {
      Respond = input => Result(input.Tick, damage: [new TargetDamage(TestWorld.P1WorkerA, workerHp, workerHp)]),
    };
    var context = new TickContext(world);

    RunPhases(context, economy, combat);

    Assert.False(context.World.Units.ContainsKey(TestWorld.P1WorkerA));
    Assert.Equal(FakeEconomyRules.TestInitialBalance + ResourceAmount.OfWood(WoodPerGather), context.World.Players[PlayerId.One].Resources);
  }

  [Fact]
  public void Commit_DamageFromADifferentHp_IsIncoherentAndThrows()
  {
    var (world, _, _, _) = CombatWorld();
    var context = new TickContext(world);
    int wrongHp = world.Units[TestWorld.P2Worker].Hp + SomeDamage;

    Assert.Throws<InvalidOperationException>(() => new CombatAdapter(new FakeCombatCalculator())
        .Commit(context, Result(context.Tick, damage: [new TargetDamage(TestWorld.P2Worker, wrongHp, SomeDamage)])));
  }

  /// <summary>Fases 4, 5 y 6 con los dos adapters reales (en paralelo) sobre calculadores falsos.</summary>
  private static void RunPhases(TickContext context, FakeEconomyCalculator economyCalculator, FakeCombatCalculator combatCalculator)
  {
    var economy = new EconomyAdapter(economyCalculator, new FakeMovementRules());
    var combat = new CombatAdapter(combatCalculator);
    ITickPhase[] phases =
    [
      new SnapshotPhase(),
      new EconomyCombatLaunchPhase(economy, combat, runInParallel: true),
      new BarrierAndCommitPhase(economy, combat, TestTimeout, TimeProvider.System),
    ];
    foreach (ITickPhase phase in phases)
    {
      phase.Execute(context);
    }
  }
}
