using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Resources;
using Rts.Simulation.Engine;
using Rts.Simulation.Phases;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Phases;

public class ProductionPhaseTests
{
  private const int TrainingTicks = 10;
  private const int AlmostNoFood = 1;
  private static readonly GridCell P1CenterAnchor = new(2, 2);
  private static readonly GridCell TreeCell = new(10, 20);
  private static readonly GridCell P1WorkerACell = new(7, 3);
  private static readonly GridCell SpawnCell = new(6, 2);
  private static readonly PositionXZ SpawnPosition = new(6.5, 2.5);
  private static readonly ProductionRequest WorkerRequest = new(CommandSequence.First, TestWorld.P1Center, PlayerId.One, UnitType.Worker);

  private static ProductionPhase Phase(FakeProductionCalculator calculator) =>
      new(calculator, new FakeEconomyRules(), new FakeCombatRules());

  private static FakeProductionCalculator Responding(
      ProductionStart[]? starts = null, SpawnProposal[]? spawns = null, ProducerState[]? updates = null, IDomainEventPayload[]? events = null) =>
      new() { Respond = input => new ProductionTickResult(input.Tick, [.. starts ?? []], [.. spawns ?? []], [.. updates ?? []], [.. events ?? []]) };

  /// <summary>El Centro Principal de P1 con la solicitud de un trabajador ya en curso.</summary>
  private static ProducerState TrainingWorker(WorldState world, TickId startedAt)
  {
    ProducerState idle = world.Buildings[TestWorld.P1Center].Production!;
    var job = new ProductionJob(WorkerRequest, FakeEconomyRules.WorkerCost.Food, startedAt, TrainingTicks);
    return new ProducerState(idle.ProducerId, idle.Owner, idle.ProducerType, idle.Footprint, idle.OperationalFromTick, job, []);
  }

  [Fact]
  public void GameplayAlreadyOver_DoesNotRun()
  {
    WorldState world = TestWorld.Create();
    var context = new TickContext(world.With(gameplayResult: MatchResult.Draw(world.Tick)));
    var calculator = new FakeProductionCalculator();

    Phase(calculator).Execute(context);

    Assert.Empty(calculator.Inputs);
  }

  [Fact]
  public void Input_HasEveryProducerTheFoodOfEachPlayerAndTheOccupiedCells()
  {
    WorldState world = TestWorld.Create();
    var calculator = new FakeProductionCalculator();

    Phase(calculator).Execute(new TickContext(world));

    ProductionTickInput input = Assert.Single(calculator.Inputs);
    Assert.Equal([TestWorld.P1Center, TestWorld.P2Center], input.Producers.Select(producer => producer.ProducerId));
    int food = FakeEconomyRules.TestInitialBalance.Food;
    Assert.Equal([new PlayerFood(PlayerId.One, food), new PlayerFood(PlayerId.Two, food)], input.Food);
    Assert.Contains(P1CenterAnchor, input.Spawn.StructureCells);
    Assert.Contains(TreeCell, input.Spawn.TreeReservedCells);
    Assert.Contains(P1WorkerACell, input.Spawn.UnitCells);
  }

  [Fact]
  public void Start_ChargesTheFoodAndActivatesTheJob()
  {
    WorldState world = TestWorld.Create();
    var context = new TickContext(world);
    ProducerState training = TrainingWorker(world, context.Tick);

    Phase(Responding(
        starts: [new ProductionStart(TestWorld.P1Center, PlayerId.One, CommandSequence.First, UnitType.Worker, FakeEconomyRules.WorkerCost.Food)],
        updates: [training])).Execute(context);

    Assert.Equal(FakeEconomyRules.TestInitialBalance - FakeEconomyRules.WorkerCost, context.World.Players[PlayerId.One].Resources);
    Assert.Equal(FakeEconomyRules.TestInitialBalance, context.World.Players[PlayerId.Two].Resources);
    Assert.Equal(training, context.World.Buildings[TestWorld.P1Center].Production);
  }

  [Fact]
  public void Spawn_CreatesAnIdleUnitWithANewIdAndMaxHpAndAnnouncesItBeforeProductionChanged()
  {
    WorldState world = TestWorld.Create();
    var context = new TickContext(world);
    EntityId newId = world.LastEntityId.Next();
    var changed = new ProductionChanged(TestWorld.P1Center, PlayerId.One, ProducerStatus.Idle, null, null, []);

    Phase(Responding(
        spawns: [new SpawnProposal(TestWorld.P1Center, PlayerId.One, CommandSequence.First, UnitType.Worker, SpawnCell, SpawnPosition)],
        events: [changed])).Execute(context);

    UnitEntity unit = context.World.Units[newId];
    Assert.Equal(PlayerId.One, unit.Owner);
    Assert.Equal(SpawnPosition, unit.Position);
    Assert.Equal(FakeCombatRules.WorkerMaxHp, unit.Hp);
    Assert.True(unit.Order.IsIdle);
    Assert.Equal(context.Tick, unit.ReadyAtTick);
    Assert.Equal(newId, context.World.LastEntityId);
    Assert.Equal(
        [new UnitSpawned(newId, PlayerId.One, UnitType.Worker, SpawnPosition, FakeCombatRules.WorkerMaxHp), changed],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void StartWithAWrongCostOrWithoutEnoughFood_IsIncoherentAndThrows()
  {
    WorldState world = TestWorld.Create();
    var wrongCost = new ProductionStart(TestWorld.P1Center, PlayerId.One, CommandSequence.First, UnitType.Worker, FakeEconomyRules.SoldierCost.Food);
    var rightCost = wrongCost with { PaidFood = FakeEconomyRules.WorkerCost.Food };
    WorldState hungry = world.With(players: world.Players.SetItem(PlayerId.One, new PlayerState(PlayerId.One, ResourceAmount.OfFood(AlmostNoFood))));

    Assert.Throws<InvalidOperationException>(() => Phase(Responding(starts: [wrongCost])).Execute(new TickContext(world)));
    Assert.Throws<InvalidOperationException>(() => Phase(Responding(starts: [rightCost])).Execute(new TickContext(hungry)));
  }
}
