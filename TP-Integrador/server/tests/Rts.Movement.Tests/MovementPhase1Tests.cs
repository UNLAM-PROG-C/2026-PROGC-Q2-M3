using Rts.Movement.Configuration;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Time;

namespace Rts.Movement.Tests;

public class MovementPhase1Tests
{
  private static readonly GridSpec Grid = new(cellSize: 1.0, columns: 10, rows: 10);
  private static readonly TickRate DefaultTickRate = new(20);

  private static MovementRules CreateRules(MovementConfig? config = null) =>
      new(config ?? ContractsTests.ValidConfig(), DefaultTickRate);

  private static UnitMovementState CreateIdleWorker(PositionXZ position, int id = 1) =>
      new(new EntityId(id), PlayerId.One, UnitType.Worker, position, MovementOrder.Idle, default, MapRevision.Initial);

  [Fact]
  public void GetDistancePerTick_DividesSpeedByTickRate()
  {
    MovementRules rules = CreateRules();

    Assert.Equal(0.15, rules.GetDistancePerTick(UnitType.Worker), precision: 6);
    Assert.Equal(0.20, rules.GetDistancePerTick(UnitType.Soldier), precision: 6);
  }

  [Fact]
  public void ValidateRoute_ClearPath_IsValid()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    MovementRules rules = CreateRules();

    RouteValidation result = rules.ValidateRoute(
        map, new PositionXZ(1.5, 1.5), [new PositionXZ(5.5, 1.5), new PositionXZ(5.5, 5.5)]);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateRoute_CrossesBlockedCell_IsInvalidWithSegmentIndex()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, [new GridCell(3, 1)]);
    MovementRules rules = CreateRules();

    RouteValidation result = rules.ValidateRoute(
        map, new PositionXZ(1.5, 1.5), [new PositionXZ(2.5, 1.5), new PositionXZ(5.5, 1.5)]);

    Assert.False(result.IsValid);
    Assert.Equal(MovementErrors.InvalidPath, result.Error);
    Assert.Equal(1, result.FailedSegmentIndex);
  }

  [Fact]
  public void ValidateRoute_CutsBlockedCornerDiagonally_IsInvalid()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, [new GridCell(2, 1)]);
    MovementRules rules = CreateRules();

    RouteValidation result = rules.ValidateRoute(
        map, new PositionXZ(1.5, 1.5), [new PositionXZ(2.5, 2.5)]);

    Assert.False(result.IsValid);
    Assert.Equal(MovementErrors.InvalidPath, result.Error);
  }

  [Fact]
  public void ValidateRoute_RunsAlongBlockedCellEdge_IsInvalid()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, [new GridCell(2, 2)]);
    MovementRules rules = CreateRules();

    RouteValidation result = rules.ValidateRoute(
        map, new PositionXZ(1.5, 2.0), [new PositionXZ(3.5, 2.0)]);

    Assert.False(result.IsValid);
    Assert.Equal(MovementErrors.InvalidPath, result.Error);
  }

  [Fact]
  public void EvaluateUnitOrder_ValidMove_IsAccepted()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    UnitMovementState worker = CreateIdleWorker(new PositionXZ(1.5, 1.5));
    var dest = new PositionXZ(4.5, 1.5);
    var req = new UnitOrderRequest(worker, UnitOrderKind.Move, dest, null, [dest], map);

    UnitOrderEvaluation eval = CreateRules().EvaluateUnitOrder(req);

    Assert.True(eval.IsAccepted);
    Assert.Equal([dest], eval.AcceptedRoute);
  }

  [Fact]
  public void EvaluateUnitOrder_IncompatibleOrder_Rejected()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    UnitMovementState worker = CreateIdleWorker(new PositionXZ(1.5, 1.5));
    MovementTarget target = MovementTarget.AtPoint(new EntityId(99), new PositionXZ(2.0, 1.5));
    var req = new UnitOrderRequest(worker, UnitOrderKind.Attack, null, target, [new PositionXZ(2.0, 1.5)], map);

    UnitOrderEvaluation eval = CreateRules().EvaluateUnitOrder(req);

    Assert.False(eval.IsAccepted);
    Assert.Equal(CommonErrors.IncompatibleEntity, eval.Error);
  }

  [Fact]
  public void EvaluateUnitOrder_DestinationOutOfBounds_Rejected()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    UnitMovementState worker = CreateIdleWorker(new PositionXZ(1.5, 1.5));
    var dest = new PositionXZ(25.0, 1.5);
    var req = new UnitOrderRequest(worker, UnitOrderKind.Move, dest, null, [dest], map);

    UnitOrderEvaluation eval = CreateRules().EvaluateUnitOrder(req);

    Assert.False(eval.IsAccepted);
    Assert.Equal(MovementErrors.DestinationOutOfBounds, eval.Error);
  }

  [Fact]
  public void EvaluateUnitOrder_TooManyWaypoints_Rejected()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    MovementConfig config = ContractsTests.ValidConfig() with { MaxWaypointsPerRoute = 1 };
    UnitMovementState worker = CreateIdleWorker(new PositionXZ(1.5, 1.5));
    var dest = new PositionXZ(4.5, 1.5);
    var req = new UnitOrderRequest(
        worker, UnitOrderKind.Move, dest, null, [new PositionXZ(2.5, 1.5), dest], map);

    UnitOrderEvaluation eval = CreateRules(config).EvaluateUnitOrder(req);

    Assert.False(eval.IsAccepted);
    Assert.Equal(MovementErrors.TooManyWaypoints, eval.Error);
  }

  [Fact]
  public void EvaluateUnitOrder_RouteDoesNotReachGoal_Rejected()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    UnitMovementState worker = CreateIdleWorker(new PositionXZ(1.5, 1.5));
    var dest = new PositionXZ(8.5, 1.5);
    var req = new UnitOrderRequest(
        worker, UnitOrderKind.Move, dest, null, [new PositionXZ(3.5, 1.5)], map);

    UnitOrderEvaluation eval = CreateRules().EvaluateUnitOrder(req);

    Assert.False(eval.IsAccepted);
    Assert.Equal(MovementErrors.RouteDoesNotReachGoal, eval.Error);
  }

  [Fact]
  public void CalculateTick_AdvancesAcrossMultipleWaypointsWithinBudget()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    var calculator = new MovementCalculator(CreateRules());
    var dest = new PositionXZ(2.0, 1.0);
    var movingWorker = new UnitMovementState(
        new EntityId(1),
        PlayerId.One,
        UnitType.Worker,
        new PositionXZ(1.0, 1.0),
        MovementOrder.Move(new OrderId(10), dest),
        [new PositionXZ(1.05, 1.0), dest],
        MapRevision.Initial);

    MovementTickResult result = calculator.CalculateTick(
        new MovementTickInput(new TickId(1), map, [movingWorker], default));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.Equal(1.15, updated.Position.X, precision: 6);
    Assert.Equal(1.0, updated.Position.Z, precision: 6);
    Assert.Equal([dest], updated.RemainingWaypoints);
    Assert.Empty(result.OrdersEnded);
  }

  [Fact]
  public void CalculateTick_CompletesMoveOrderOnFinalWaypoint()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    var calculator = new MovementCalculator(CreateRules());
    var dest = new PositionXZ(1.10, 1.0);
    var movingWorker = new UnitMovementState(
        new EntityId(1),
        PlayerId.One,
        UnitType.Worker,
        new PositionXZ(1.0, 1.0),
        MovementOrder.Move(new OrderId(10), dest),
        [dest],
        MapRevision.Initial);

    MovementTickResult result = calculator.CalculateTick(
        new MovementTickInput(new TickId(1), map, [movingWorker], default));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.Equal(dest, updated.Position);
    Assert.True(updated.Order.IsIdle);
    Assert.True(updated.RemainingWaypoints.IsEmpty);

    OrderEnded ended = Assert.Single(result.OrdersEnded);
    Assert.Equal(OrderEndReason.Completed, ended.Reason);

    UnitOrderChanged evt = Assert.IsType<UnitOrderChanged>(Assert.Single(result.ProposedEvents));
    Assert.Equal(UnitOrderKind.Idle, evt.Kind);
  }

  [Fact]
  public void CalculateTick_PermutingInputUnitsProducesIdenticalResult()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    var calculator = new MovementCalculator(CreateRules());
    var dest = new PositionXZ(5.0, 1.0);
    var u1 = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1.0, 1.0),
        MovementOrder.Move(new OrderId(1), dest), [dest], MapRevision.Initial);
    var u2 = new UnitMovementState(
        new EntityId(2), PlayerId.Two, UnitType.Soldier, new PositionXZ(1.0, 2.0),
        MovementOrder.Move(new OrderId(2), dest), [dest], MapRevision.Initial);

    MovementTickResult r1 = calculator.CalculateTick(new MovementTickInput(new TickId(1), map, [u1, u2], default));
    MovementTickResult r2 = calculator.CalculateTick(new MovementTickInput(new TickId(1), map, [u2, u1], default));

    Assert.Equal(r1, r2);
  }
}
