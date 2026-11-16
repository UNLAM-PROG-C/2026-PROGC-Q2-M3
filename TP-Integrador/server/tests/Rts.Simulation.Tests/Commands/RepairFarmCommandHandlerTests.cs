using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Economy;
using Rts.Movement;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

public class RepairFarmCommandHandlerTests
{
  private const int WorkDoneSoFar = 5;
  private const int NotEnoughWood = 10;

  private static readonly GridCell FarmAnchor = new(20, 20);
  private static readonly PositionXZ Waypoint = new(19.5, 19.5);
  private static readonly EquatableArray<PositionXZ> SomeRoute = [Waypoint];

  private static CommandEnvelope Repair(PlayerId player, EntityId farm, params EntityId[] workers) =>
      new(CommandSequence.First, player, new ClientCommandId(1), new RepairFarmCommand(
          farm, MapRevision.Initial, [.. workers.Select(worker => new UnitRouteCandidate(worker, SomeRoute))]));

  private static CommandEnvelope Repair(EntityId farm, params EntityId[] workers) => Repair(PlayerId.One, farm, workers);

  private static RepairFarmCommandHandler Handler(FakeConstructionRules? construction = null, FakeMovementRules? movement = null) =>
      new(construction ?? new FakeConstructionRules(), new FakeEconomyRules(), movement ?? new FakeMovementRules());

  /// <summary>Una granja de <paramref name="owner"/> completada y agotada, todavía sin reparación. Devuelve su ID.</summary>
  private static (WorldState World, EntityId FarmId) WithDepletedFarm(WorldState world, PlayerId owner)
  {
    (WorldState repairing, EntityId farmId) = TestWorld.WithRepairingFarm(world, owner, FarmAnchor, workDone: 0);
    BuildingEntity farm = repairing.Buildings[farmId];
    var depleted = new BuildingEntity(
        farm.Id, farm.Owner, farm.Type, farm.Footprint, farm.Hp, farm.OperationalFromTick, farm.FarmEconomy,
        repair: null, production: null, towerReadyAtTick: null);
    return (repairing.With(buildings: repairing.Buildings.SetItem(farmId, depleted)), farmId);
  }

  private static ResourceAmount WoodOf(WorldState world, PlayerId player) => world.Players[player].Resources;

  [Fact]
  public void Handle_StartAndPay_ChargesOnceStartsTheRepairAndGivesTheOrders()
  {
    (WorldState world, EntityId farmId) = WithDepletedFarm(TestWorld.Create(), PlayerId.One);
    var context = new TickContext(world);
    OrderId first = world.LastOrderId.Next();
    OrderId second = first.Next();

    CommandResult result = Handler().Handle(Repair(farmId, TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.Equal(WoodOf(world, PlayerId.One) - FakeEconomyRules.FarmRepairCost, WoodOf(context.World, PlayerId.One));
    Assert.Equal(WoodOf(world, PlayerId.Two), WoodOf(context.World, PlayerId.Two));
    Assert.Equal(0, context.World.Buildings[farmId].Repair!.WorkDone);
    Assert.Equal(MovementOrder.Task(first, UnitOrderKind.RepairFarm, farmId), context.World.Units[TestWorld.P1WorkerA].Order);
    Assert.Equal(
        [
          new FarmRepairStarted(farmId),
          new UnitOrderChanged(TestWorld.P1WorkerA, first, UnitOrderKind.RepairFarm),
          new UnitOrderChanged(TestWorld.P1WorkerB, second, UnitOrderKind.RepairFarm),
        ],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void Handle_AsksConstructionWithTheFarmDataAndMovementWithItsFootprint()
  {
    var construction = new FakeConstructionRules();
    var movement = new FakeMovementRules();
    (WorldState world, EntityId farmId) = WithDepletedFarm(TestWorld.Create(), PlayerId.One);
    var context = new TickContext(world);

    Handler(construction, movement).Handle(Repair(farmId, TestWorld.P1WorkerA), context);

    Assert.Equal(
        new RepairStartCall(
            PlayerId.One,
            new RepairTargetInfo(farmId, PlayerId.One, BuildingType.Farm, IsCompleted: true, IsDepleted: true, IsRepairInProgress: false)),
        Assert.Single(construction.RepairStarts));
    var request = Assert.Single(movement.Requests);
    Assert.Equal(UnitOrderKind.RepairFarm, request.Kind);
    Assert.Equal(MovementTarget.WithFootprint(farmId, world.Buildings[farmId].Footprint), request.Target);
  }

  [Fact]
  public void Handle_Join_AddsTheWorkersWithoutChargingOrRestartingTheRepair()
  {
    (WorldState world, EntityId farmId) = TestWorld.WithRepairingFarm(TestWorld.Create(), PlayerId.One, FarmAnchor, WorkDoneSoFar);
    var context = new TickContext(world);

    CommandResult result = Handler().Handle(Repair(farmId, TestWorld.P1WorkerA), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.Equal(WoodOf(world, PlayerId.One), WoodOf(context.World, PlayerId.One));
    Assert.Equal(WorkDoneSoFar, context.World.Buildings[farmId].Repair!.WorkDone);
    Assert.IsType<UnitOrderChanged>(Assert.Single(context.Events).Payload); // Sin FarmRepairStarted.
  }

  [Fact]
  public void Handle_StartAndPayWithNoReachableWorker_RejectsEverythingWithoutCharging()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerA] = MovementErrors.RouteDoesNotReachGoal;
    (WorldState world, EntityId farmId) = WithDepletedFarm(TestWorld.Create(), PlayerId.One);
    var context = new TickContext(world);

    CommandResult result = Handler(movement: movement).Handle(Repair(farmId, TestWorld.P1WorkerA), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(CommonErrors.NoEntityAccepted, result.Error);
    Assert.Same(world.Players, context.World.Players); // Sin cobro (D-036).
    Assert.Null(context.World.Buildings[farmId].Repair);
    Assert.Empty(context.Events);
  }

  [Fact]
  public void Handle_SomeWorkersReachTheFarm_ChargesOnceAndAcceptsPartially()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerB] = MovementErrors.InvalidPath;
    (WorldState world, EntityId farmId) = WithDepletedFarm(TestWorld.Create(), PlayerId.One);
    var context = new TickContext(world);

    CommandResult result = Handler(movement: movement).Handle(Repair(farmId, TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.Equal(WoodOf(world, PlayerId.One) - FakeEconomyRules.FarmRepairCost, WoodOf(context.World, PlayerId.One));
    Assert.True(context.World.Units[TestWorld.P1WorkerB].Order.IsIdle);
  }

  [Fact]
  public void Handle_NotEnoughWood_RejectsTheWholeCommandWithEconomysError()
  {
    (WorldState depleted, EntityId farmId) = WithDepletedFarm(TestWorld.Create(), PlayerId.One);
    WorldState world = depleted.With(
        players: depleted.Players.SetItem(PlayerId.One, new PlayerState(PlayerId.One, ResourceAmount.OfWood(NotEnoughWood))));
    var context = new TickContext(world);

    CommandResult result = Handler().Handle(Repair(farmId, TestWorld.P1WorkerA), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(EconomyErrors.InsufficientResources, result.Error);
    Assert.Same(context.Previous.Players, context.World.Players);
    Assert.True(context.World.Units[TestWorld.P1WorkerA].Order.IsIdle);
    Assert.Empty(context.Events);
  }

  [Fact]
  public void Handle_FarmStillProducing_IsRejectedWithoutAskingMovement()
  {
    var movement = new FakeMovementRules();
    (WorldState world, EntityId farmId) = TestWorld.WithFarm(TestWorld.Create(), PlayerId.One, FarmAnchor);
    var context = new TickContext(world);

    CommandResult result = Handler(movement: movement).Handle(Repair(farmId, TestWorld.P1WorkerA), context);

    Assert.Equal(ConstructionErrors.FarmNotDepleted, result.Error);
    Assert.True(result.EntityResults.IsEmpty); // Falló el objetivo, no una unidad.
    Assert.Empty(movement.Requests);
  }

  [Fact]
  public void Handle_RivalFarmOrMissingTarget_IsRejected()
  {
    var construction = new FakeConstructionRules();
    (WorldState world, EntityId rivalFarm) = WithDepletedFarm(TestWorld.Create(), PlayerId.Two);
    var context = new TickContext(world);

    CommandResult rival = Handler(construction).Handle(Repair(rivalFarm, TestWorld.P1WorkerA), context);
    CommandResult missing = Handler(construction).Handle(Repair(TestWorld.Missing, TestWorld.P1WorkerA), context);

    Assert.Equal(CommonErrors.NotOwner, rival.Error);
    Assert.Equal(CommonErrors.EntityNotFound, missing.Error);
    Assert.Null(construction.RepairStarts[1].Target);
  }

  [Fact]
  public void Handle_ConstructionAsksToPayForARepairInProgress_IsACriticalFailure()
  {
    var careless = new FakeConstructionRules { ForcedRepairStart = RepairStartEvaluation.Valid(RepairStartKind.StartAndPay) };
    (WorldState world, EntityId farmId) = TestWorld.WithRepairingFarm(TestWorld.Create(), PlayerId.One, FarmAnchor, WorkDoneSoFar);

    Assert.Throws<InvalidOperationException>(
        () => Handler(careless).Handle(Repair(farmId, TestWorld.P1WorkerA), new TickContext(world)));
  }

  [Fact]
  public void Handle_ConstructionAsksToJoinARepairThatDoesNotExist_IsACriticalFailure()
  {
    var careless = new FakeConstructionRules { ForcedRepairStart = RepairStartEvaluation.Valid(RepairStartKind.Join) };
    (WorldState world, EntityId farmId) = WithDepletedFarm(TestWorld.Create(), PlayerId.One);

    Assert.Throws<InvalidOperationException>(
        () => Handler(careless).Handle(Repair(farmId, TestWorld.P1WorkerA), new TickContext(world)));
  }
}
