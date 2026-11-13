using Rts.Movement;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

public class RepathResponseCommandHandlerTests
{
  private const int GatherTicksSoFar = 7;

  private static readonly PositionXZ Detour = new(14.5, 18.5);
  private static readonly EquatableArray<PositionXZ> NewRoute = [Detour, TestWorld.SomeDestination];
  private static readonly PositionXZ SoldierPosition = new(15.5, 15.5);

  private static CommandEnvelope Repath(PlayerId player, EntityId unit, OrderId orderId) =>
      new(CommandSequence.First, player, new ClientCommandId(1), new RepathResponseCommand(unit, orderId, MapRevision.Initial, NewRoute));

  private static CommandEnvelope Repath(EntityId unit, OrderId orderId) => Repath(PlayerId.One, unit, orderId);

  /// <summary>El trabajador A con una orden Move pausada: esperando ruta nueva desde el tick 0 (04 §5).</summary>
  private static WorldState WithPausedMove(WorldState world)
  {
    WorldState moving = TestWorld.WithMovingUnit(world, TestWorld.P1WorkerA);
    UnitEntity unit = moving.Units[TestWorld.P1WorkerA];
    var paused = new UnitEntity(
        unit.Id, unit.Owner, unit.Type, unit.Position, unit.Hp, unit.Order, unit.Route, unit.RouteMapRevision,
        repathPendingSinceTick: TickId.Zero, lastPursuitRepathTick: null, unit.GatherTicksAccumulated, unit.ReadyAtTick);
    return moving.With(units: moving.Units.SetItem(unit.Id, paused));
  }

  private static RepathResponseCommandHandler Handler(FakeMovementRules? movement = null) => new(movement ?? new FakeMovementRules());

  [Fact]
  public void Handle_CurrentOrder_InstallsTheRouteKeepsTheOrderAndClearsThePause()
  {
    var context = new TickContext(WithPausedMove(TestWorld.Create()));
    MovementOrder order = context.World.Units[TestWorld.P1WorkerA].Order;
    OrderId lastOrderId = context.World.LastOrderId;

    CommandResult result = Handler().Handle(Repath(TestWorld.P1WorkerA, order.OrderId), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.True(result.EntityResults.IsEmpty); // Una sola unidad: no es un comando grupal.
    UnitEntity unit = context.World.Units[TestWorld.P1WorkerA];
    Assert.Equal(order, unit.Order); // Mismo OrderId: un repath no crea una orden.
    Assert.Equal(NewRoute, unit.Route);
    Assert.Null(unit.RepathPendingSinceTick);
    Assert.Equal(context.World.MapRevision, unit.RouteMapRevision);
    Assert.Equal(lastOrderId, context.World.LastOrderId);
    Assert.Empty(context.Events); // La orden no cambió.
  }

  [Fact]
  public void Handle_OldOrderId_IsRejectedAsStaleAndTheWorldIsUntouched()
  {
    WorldState firstMove = TestWorld.WithMovingUnit(TestWorld.Create(), TestWorld.P1WorkerA);
    OrderId previousOrder = firstMove.Units[TestWorld.P1WorkerA].Order.OrderId;
    var context = new TickContext(WithPausedMove(firstMove)); // Una segunda orden Move reemplazó a la primera.
    WorldState before = context.World;

    CommandResult result = Handler().Handle(Repath(TestWorld.P1WorkerA, previousOrder), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(MovementErrors.StaleOrder, result.Error);
    Assert.Same(before, context.World);
  }

  [Fact]
  public void Handle_LateResponseAfterStop_DoesNotReviveTheOrder()
  {
    WorldState paused = WithPausedMove(TestWorld.Create());
    OrderId canceledOrder = paused.Units[TestWorld.P1WorkerA].Order.OrderId;
    UnitEntity stopped = paused.Units[TestWorld.P1WorkerA].AsIdle();
    var context = new TickContext(paused.With(units: paused.Units.SetItem(stopped.Id, stopped)));

    CommandResult result = Handler().Handle(Repath(TestWorld.P1WorkerA, canceledOrder), context);

    Assert.Equal(MovementErrors.StaleOrder, result.Error);
    Assert.True(context.World.Units[TestWorld.P1WorkerA].Order.IsIdle);
  }

  [Fact]
  public void Handle_GatheringWorker_KeepsItsGatherCounterAndMovementGetsTheTreePoint()
  {
    var movement = new FakeMovementRules();
    var context = new TickContext(TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, GatherTicksSoFar));
    UnitEntity worker = context.World.Units[TestWorld.P1WorkerA];
    PositionXZ treePosition = context.World.Trees[TestWorld.Tree].Position;

    Handler(movement).Handle(Repath(TestWorld.P1WorkerA, worker.Order.OrderId), context);

    Assert.Equal(GatherTicksSoFar, context.World.Units[TestWorld.P1WorkerA].GatherTicksAccumulated);
    Assert.Equal(MovementTarget.AtPoint(TestWorld.Tree, treePosition), Assert.Single(movement.RepathRequests).Target);
  }

  [Fact]
  public void Handle_PursuingSoldier_KeepsItsPursuitMarkAndMovementGetsTheTargetPoint()
  {
    var movement = new FakeMovementRules();
    (WorldState world, EntityId soldierId) = TestWorld.WithSoldier(TestWorld.Create(), PlayerId.One, SoldierPosition, TestWorld.P2Worker);
    var context = new TickContext(world);
    UnitEntity soldier = world.Units[soldierId];

    Handler(movement).Handle(Repath(soldierId, soldier.Order.OrderId), context);

    Assert.Equal(soldier.LastPursuitRepathTick, context.World.Units[soldierId].LastPursuitRepathTick);
    Assert.Equal(
        MovementTarget.AtPoint(TestWorld.P2Worker, world.Units[TestWorld.P2Worker].Position),
        Assert.Single(movement.RepathRequests).Target);
  }

  [Fact]
  public void Handle_MoveOrder_MovementGetsNoTargetAndTheCurrentMap()
  {
    var movement = new FakeMovementRules();
    var context = new TickContext(WithPausedMove(TestWorld.Create()));
    OrderId orderId = context.World.Units[TestWorld.P1WorkerA].Order.OrderId;

    Handler(movement).Handle(Repath(TestWorld.P1WorkerA, orderId), context);

    RepathResponseCall call = Assert.Single(movement.RepathRequests);
    Assert.Null(call.Target);
    Assert.Equal(orderId, call.OrderId);
    Assert.Equal(NewRoute, call.Waypoints);
    Assert.Same(context.Navigation, call.Map);
  }

  [Fact]
  public void Handle_InvalidRoute_UnitStaysPausedWithItsOldRoute()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerA] = MovementErrors.InvalidPath;
    var context = new TickContext(WithPausedMove(TestWorld.Create()));
    UnitEntity before = context.World.Units[TestWorld.P1WorkerA];

    CommandResult result = Handler(movement).Handle(Repath(TestWorld.P1WorkerA, before.Order.OrderId), context);

    Assert.Equal(MovementErrors.InvalidPath, result.Error);
    Assert.Same(before, context.World.Units[TestWorld.P1WorkerA]);
  }

  [Fact]
  public void Handle_RivalOrMissingUnit_IsRejectedWithoutAskingMovement()
  {
    var movement = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    var anyOrder = new OrderId(1);

    CommandResult rival = Handler(movement).Handle(Repath(PlayerId.One, TestWorld.P2Worker, anyOrder), context);
    CommandResult missing = Handler(movement).Handle(Repath(PlayerId.One, TestWorld.Missing, anyOrder), context);

    Assert.Equal(CommonErrors.NotOwner, rival.Error);
    Assert.Equal(CommonErrors.EntityNotFound, missing.Error);
    Assert.Empty(movement.RepathRequests);
  }

  [Fact]
  public void Handle_TaskOrderOnAMissingTarget_IsACriticalFailure()
  {
    (WorldState world, EntityId soldierId) = TestWorld.WithSoldier(TestWorld.Create(), PlayerId.One, SoldierPosition, TestWorld.Missing);
    var context = new TickContext(world);

    Assert.Throws<InvalidOperationException>(
        () => Handler().Handle(Repath(soldierId, world.Units[soldierId].Order.OrderId), context));
  }
}
