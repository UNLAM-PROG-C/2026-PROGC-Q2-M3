using Rts.Movement.Configuration;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Time;

namespace Rts.Movement.Tests;

public class MovementPhase2Tests
{
  private static readonly GridSpec Grid = new(cellSize: 1.0, columns: 10, rows: 10);
  private static readonly TickRate DefaultTickRate = new(20);

  private static MovementRules CreateRules(MovementConfig? config = null) =>
      new(config ?? ContractsTests.ValidConfig(), DefaultTickRate);

  private static MovementCalculator CreateCalculator(MovementConfig? config = null)
  {
    MovementConfig cfg = config ?? ContractsTests.ValidConfig();
    return new MovementCalculator(CreateRules(cfg), cfg);
  }

  [Fact]
  public void CheckReachability_OpenPathToPointAndFootprint_ReturnsTrue()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    MovementRules rules = CreateRules();
    MovementTarget pointTarget = MovementTarget.AtPoint(new EntityId(10), new PositionXZ(8.5, 8.5));
    MovementTarget footprintTarget = MovementTarget.WithFootprint(
        new EntityId(20), new Footprint(new GridCell(5, 5), new FootprintSize(2, 2)));

    Assert.True(rules.CheckReachability(map, new PositionXZ(1.5, 1.5), pointTarget, 1.0));
    Assert.True(rules.CheckReachability(map, new PositionXZ(1.5, 1.5), footprintTarget, 1.0));
  }

  [Fact]
  public void CheckReachability_EnclosedByFences_ReturnsFalse()
  {
    GridCell[] fenceRing =
    [
      new GridCell(4, 4), new GridCell(5, 4), new GridCell(6, 4),
      new GridCell(4, 5),                     new GridCell(6, 5),
      new GridCell(4, 6), new GridCell(5, 6), new GridCell(6, 6),
    ];
    var map = new NavigationGrid(Grid, MapRevision.Initial, fenceRing);
    MovementTarget targetInside = MovementTarget.AtPoint(new EntityId(10), new PositionXZ(5.5, 5.5));

    bool reachable = CreateRules().CheckReachability(map, new PositionXZ(1.5, 1.5), targetInside, 0.4);

    Assert.False(reachable);
  }

  [Fact]
  public void CheckReachability_DiagonalBlockedByOrthogonalWalls_ReturnsFalse()
  {
    GridCell[] diagonalBarrier = [new GridCell(1, 0), new GridCell(0, 1)];
    var map = new NavigationGrid(Grid, MapRevision.Initial, diagonalBarrier);
    MovementTarget target = MovementTarget.AtPoint(new EntityId(10), new PositionXZ(5.5, 5.5));

    bool reachable = CreateRules().CheckReachability(map, new PositionXZ(0.5, 0.5), target, 0.5);

    Assert.False(reachable);
  }

  [Fact]
  public void EvaluateRepathResponse_StaleOrderOrShortRoute_Rejected()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    MovementRules rules = CreateRules();
    var dest = new PositionXZ(5.5, 1.5);
    var unit = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1.5, 1.5),
        MovementOrder.Move(new OrderId(10), dest), [dest], MapRevision.Initial);

    UnitOrderEvaluation stale = rules.EvaluateRepathResponse(unit, new OrderId(99), null, [dest], map);
    UnitOrderEvaluation shortRoute = rules.EvaluateRepathResponse(
        unit, new OrderId(10), null, [new PositionXZ(2.5, 1.5)], map);
    UnitOrderEvaluation valid = rules.EvaluateRepathResponse(unit, new OrderId(10), null, [dest], map);

    Assert.Equal(MovementErrors.StaleOrder, stale.Error);
    Assert.Equal(MovementErrors.RouteDoesNotReachGoal, shortRoute.Error);
    Assert.True(valid.IsAccepted);
  }

  [Fact]
  public void CalculateTick_NewWallInvalidatesRoute_EntersRepathPendingAndEmitsEvent()
  {
    var mapWithWall = new NavigationGrid(Grid, new MapRevision(2), [new GridCell(3, 1)]);
    var dest = new PositionXZ(5.5, 1.5);
    var unit = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1.5, 1.5),
        MovementOrder.Move(new OrderId(10), dest), [dest], MapRevision.Initial);

    MovementTickResult result = CreateCalculator().CalculateTick(
        new MovementTickInput(new TickId(5), mapWithWall, [unit], default));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.True(updated.IsRepathPending);
    Assert.Equal(new TickId(5), updated.RepathPendingSinceTick);

    RepathRequested evt = Assert.IsType<RepathRequested>(Assert.Single(result.ProposedEvents));
    Assert.True(evt.IsPaused);
    Assert.Equal(dest, evt.Destination);
  }

  [Fact]
  public void CalculateTick_MapRevisionChangedButRouteStillValid_UpdatesRevisionAndMoves()
  {
    var mapWithDistantWall = new NavigationGrid(Grid, new MapRevision(3), [new GridCell(9, 9)]);
    var dest = new PositionXZ(5.5, 1.5);
    var unit = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1.5, 1.5),
        MovementOrder.Move(new OrderId(10), dest), [dest], MapRevision.Initial);

    MovementTickResult result = CreateCalculator().CalculateTick(
        new MovementTickInput(new TickId(5), mapWithDistantWall, [unit], default));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.False(updated.IsRepathPending);
    Assert.Equal(new MapRevision(3), updated.RouteMapRevision);
    Assert.True(updated.Position.X > 1.5);
  }

  [Fact]
  public void CalculateTick_RepathTimeoutReached_EndsOrderWithRepathTimeout()
  {
    var map = new NavigationGrid(Grid, new MapRevision(2), []);
    var dest = new PositionXZ(5.5, 1.5);
    var unit = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1.5, 1.5),
        MovementOrder.Move(new OrderId(10), dest), [dest], MapRevision.Initial,
        repathPendingSinceTick: new TickId(10));

    MovementTickResult result = CreateCalculator().CalculateTick(
        new MovementTickInput(new TickId(50), map, [unit], default));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.True(updated.Order.IsIdle);
    OrderEnded ended = Assert.Single(result.OrdersEnded);
    Assert.Equal(OrderEndReason.RepathTimeout, ended.Reason);
  }

  [Fact]
  public void CalculateTick_TaskTargetMissing_EndsOrderWithTargetLost()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    var unit = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1.5, 1.5),
        MovementOrder.Task(new OrderId(10), UnitOrderKind.GatherWood, new EntityId(99)),
        [new PositionXZ(4.0, 1.5)], MapRevision.Initial);

    MovementTickResult result = CreateCalculator().CalculateTick(
        new MovementTickInput(new TickId(1), map, [unit], default));

    OrderEnded ended = Assert.Single(result.OrdersEnded);
    Assert.Equal(OrderEndReason.TargetLost, ended.Reason);
  }

  [Fact]
  public void CalculateTick_TaskArrivesInRange_KeepsTaskActiveAndClearsWaypoints()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    var treeId = new EntityId(50);
    MovementTarget tree = MovementTarget.AtPoint(treeId, new PositionXZ(2.0, 1.5));
    var unit = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(0.95, 1.5),
        MovementOrder.Task(new OrderId(10), UnitOrderKind.GatherWood, treeId),
        [new PositionXZ(1.5, 1.5)], MapRevision.Initial);

    MovementTickResult result = CreateCalculator().CalculateTick(
        new MovementTickInput(new TickId(1), map, [unit], [tree]));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.Equal(UnitOrderKind.GatherWood, updated.Order.Kind);
    Assert.True(updated.RemainingWaypoints.IsEmpty);
    Assert.Empty(result.OrdersEnded);
  }

  [Fact]
  public void CalculateTick_AttackPursuit_DirectLineReplacesRouteAndAdvances()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, []);
    var enemyId = new EntityId(80);
    MovementTarget movedEnemy = MovementTarget.AtPoint(enemyId, new PositionXZ(1.5, 6.0));
    var soldier = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Soldier, new PositionXZ(1.5, 1.5),
        MovementOrder.Task(new OrderId(10), UnitOrderKind.Attack, enemyId),
        [new PositionXZ(5.0, 1.5)], MapRevision.Initial);

    MovementTickResult result = CreateCalculator().CalculateTick(
        new MovementTickInput(new TickId(1), map, [soldier], [movedEnemy]));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.Equal(1.5, updated.Position.X, precision: 6);
    Assert.Equal(1.7, updated.Position.Z, precision: 6);
    Assert.Equal([new PositionXZ(1.5, 6.0)], updated.RemainingWaypoints);
  }

  [Fact]
  public void CalculateTick_AttackPursuit_BlockedDirectLineRequestsUnpausedRepath()
  {
    var map = new NavigationGrid(Grid, MapRevision.Initial, [new GridCell(1, 3)]);
    var enemyId = new EntityId(80);
    MovementTarget movedEnemy = MovementTarget.AtPoint(enemyId, new PositionXZ(1.5, 6.0));
    var soldier = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Soldier, new PositionXZ(1.5, 1.5),
        MovementOrder.Task(new OrderId(10), UnitOrderKind.Attack, enemyId),
        [new PositionXZ(3.5, 1.5)], MapRevision.Initial);

    MovementTickResult result = CreateCalculator().CalculateTick(
        new MovementTickInput(new TickId(20), map, [soldier], [movedEnemy]));

    UnitMovementState updated = Assert.Single(result.UnitUpdates);
    Assert.False(updated.IsRepathPending);
    Assert.Equal(new TickId(20), updated.LastPursuitRepathTick);
    Assert.True(updated.Position.X > 1.5);

    RepathRequested evt = Assert.IsType<RepathRequested>(Assert.Single(result.ProposedEvents));
    Assert.False(evt.IsPaused);
    Assert.Equal(enemyId, evt.TargetId);
  }
}

