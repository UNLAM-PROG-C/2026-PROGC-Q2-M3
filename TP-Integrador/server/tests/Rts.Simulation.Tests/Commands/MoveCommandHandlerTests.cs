using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

public class MoveCommandHandlerTests
{
  private const int GatherTicksSoFar = 7;

  private static readonly PositionXZ Waypoint = new(12.5, 12.5);
  private static readonly EquatableArray<PositionXZ> SomeRoute = [Waypoint, TestWorld.SomeDestination];
  private static readonly GridCell P1CenterAnchor = new(2, 2);
  private static readonly GridCell FreeCell = new(20, 20);

  private static CommandEnvelope Move(PlayerId player, params EntityId[] units) =>
      new(CommandSequence.First, player, new ClientCommandId(1), new MoveCommand(
          TestWorld.SomeDestination, MapRevision.Initial, [.. units.Select(unit => new UnitRouteCandidate(unit, SomeRoute))]));

  [Fact]
  public void Handle_IdleUnit_GetsNewOrderIdOrderAndValidatedRoute()
  {
    var rules = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    OrderId expectedId = context.World.LastOrderId.Next();

    CommandResult result = new MoveCommandHandler(rules).Handle(Move(PlayerId.One, TestWorld.P1WorkerA), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    UnitEntity unit = context.World.Units[TestWorld.P1WorkerA];
    Assert.Equal(UnitOrderKind.Move, unit.Order.Kind);
    Assert.Equal(expectedId, unit.Order.OrderId);
    Assert.Equal(TestWorld.SomeDestination, unit.Order.Destination);
    Assert.Equal(SomeRoute, unit.Route);
    Assert.Equal(context.World.MapRevision, unit.RouteMapRevision);
    Assert.Equal(expectedId, context.World.LastOrderId);
    DomainEvent changed = Assert.Single(context.Events);
    Assert.Equal(new UnitOrderChanged(TestWorld.P1WorkerA, expectedId, UnitOrderKind.Move), changed.Payload);
  }

  [Fact]
  public void Handle_TwoUnits_GetConsecutiveOrderIdsAndEventsInCommandOrder()
  {
    var context = new TickContext(TestWorld.Create());
    OrderId first = context.World.LastOrderId.Next();
    OrderId second = first.Next();

    new MoveCommandHandler(new FakeMovementRules()).Handle(Move(PlayerId.One, TestWorld.P1WorkerB, TestWorld.P1WorkerA), context);

    Assert.Equal(
        [
          new UnitOrderChanged(TestWorld.P1WorkerB, first, UnitOrderKind.Move),
          new UnitOrderChanged(TestWorld.P1WorkerA, second, UnitOrderKind.Move),
        ],
        context.Events.Select(stamped => stamped.Payload));
    Assert.Equal(second, context.World.LastOrderId);
  }

  [Fact]
  public void Handle_GatheringUnit_ReplacesTheTaskAndDiscardsGatherCounter()
  {
    var context = new TickContext(TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, GatherTicksSoFar));

    new MoveCommandHandler(new FakeMovementRules()).Handle(Move(PlayerId.One, TestWorld.P1WorkerA), context);

    UnitEntity unit = context.World.Units[TestWorld.P1WorkerA];
    Assert.Equal(UnitOrderKind.Move, unit.Order.Kind);
    Assert.Equal(0, unit.GatherTicksAccumulated);
  }

  [Fact]
  public void Handle_RouteRejectedByMovement_UnitKeepsItsPreviousOrder()
  {
    var rules = new FakeMovementRules();
    rules.Rejections[TestWorld.P1WorkerB] = MovementErrors.InvalidPath;
    WorldState world = TestWorld.WithMovingUnit(TestWorld.Create(), TestWorld.P1WorkerB);
    MovementOrderSnapshot before = MovementOrderSnapshot.Of(world.Units[TestWorld.P1WorkerB]);
    var context = new TickContext(world);

    CommandResult result = new MoveCommandHandler(rules).Handle(Move(PlayerId.One, TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.Equal(EntityCommandResult.Rejected(TestWorld.P1WorkerB, MovementErrors.InvalidPath), result.EntityResults[1]);
    Assert.Equal(before, MovementOrderSnapshot.Of(context.World.Units[TestWorld.P1WorkerB]));
    Assert.Single(context.Events);
  }

  [Fact]
  public void Handle_ForeignOrMissingUnit_IsRejectedWithoutAskingMovement()
  {
    var rules = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;

    CommandResult result = new MoveCommandHandler(rules).Handle(Move(PlayerId.One, TestWorld.P2Worker, TestWorld.Missing), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(CommonErrors.NoEntityAccepted, result.Error);
    Assert.Equal(
        [
          EntityCommandResult.Rejected(TestWorld.P2Worker, CommonErrors.NotOwner),
          EntityCommandResult.Rejected(TestWorld.Missing, CommonErrors.EntityNotFound),
        ],
        result.EntityResults);
    Assert.Empty(rules.Requests);
    Assert.Same(before, context.World);
    Assert.Empty(context.Events);
  }

  [Fact]
  public void Handle_AsksMovementWithTheCurrentMapWhereBuildingsBlock()
  {
    var rules = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());

    new MoveCommandHandler(rules).Handle(Move(PlayerId.One, TestWorld.P1WorkerA), context);

    UnitOrderRequest request = Assert.Single(rules.Requests);
    Assert.Equal(UnitOrderKind.Move, request.Kind);
    Assert.Equal(TestWorld.SomeDestination, request.Destination);
    Assert.Equal(SomeRoute, request.Waypoints);
    Assert.Equal(context.World.MapRevision, request.Map.Revision);
    Assert.True(request.Map.IsBlocked(P1CenterAnchor));
    Assert.False(request.Map.IsBlocked(FreeCell));
  }

  [Fact]
  public void Navigation_IsReusedWhileTheMapRevisionDoesNotChange()
  {
    var context = new TickContext(TestWorld.Create());

    Assert.Same(context.Navigation, context.Navigation);
  }

  /// <summary>Lo que define la orden vigente de una unidad (para comparar antes y después).</summary>
  private sealed record MovementOrderSnapshot(OrderId OrderId, UnitOrderKind Kind, EquatableArray<PositionXZ> Route)
  {
    public static MovementOrderSnapshot Of(UnitEntity unit) => new(unit.Order.OrderId, unit.Order.Kind, unit.Route);
  }
}
