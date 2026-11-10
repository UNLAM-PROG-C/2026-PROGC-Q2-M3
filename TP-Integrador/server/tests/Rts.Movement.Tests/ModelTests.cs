using Rts.Movement.Model;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement.Tests;

public class ModelTests
{
  private static readonly GridSpec Grid = new(cellSize: 1.0, columns: 20, rows: 20);

  [Theory]
  [InlineData(UnitType.Worker, UnitOrderKind.Move, true)]
  [InlineData(UnitType.Worker, UnitOrderKind.GatherWood, true)]
  [InlineData(UnitType.Worker, UnitOrderKind.Build, true)]
  [InlineData(UnitType.Worker, UnitOrderKind.RepairFarm, true)]
  [InlineData(UnitType.Worker, UnitOrderKind.Attack, false)]
  [InlineData(UnitType.Soldier, UnitOrderKind.Move, true)]
  [InlineData(UnitType.Soldier, UnitOrderKind.Attack, true)]
  [InlineData(UnitType.Soldier, UnitOrderKind.GatherWood, false)]
  [InlineData(UnitType.Soldier, UnitOrderKind.Build, false)]
  [InlineData(UnitType.Soldier, UnitOrderKind.RepairFarm, false)]
  [InlineData(UnitType.Soldier, UnitOrderKind.Idle, true)]
  public void Compatibility_MatchesDecidedTable(UnitType unitType, UnitOrderKind kind, bool expected)
  {
    Assert.Equal(expected, UnitOrderCompatibility.IsAllowed(unitType, kind));
  }

  [Fact]
  public void MovementOrder_ShapesAreEnforced()
  {
    MovementOrder move = MovementOrder.Move(new OrderId(1), new PositionXZ(3, 3));
    MovementOrder attack = MovementOrder.Task(new OrderId(2), UnitOrderKind.Attack, new EntityId(9));

    Assert.Equal(UnitOrderKind.Move, move.Kind);
    Assert.Null(move.TargetId);
    Assert.Equal(new EntityId(9), attack.TargetId);
    Assert.True(MovementOrder.Idle.IsIdle);
    Assert.Throws<ArgumentException>(() => MovementOrder.Move(OrderId.None, new PositionXZ(3, 3)));
    Assert.Throws<ArgumentException>(() => MovementOrder.Task(new OrderId(3), UnitOrderKind.Move, new EntityId(9)));
    Assert.Throws<ArgumentException>(() => MovementOrder.Task(new OrderId(3), UnitOrderKind.Attack, EntityId.None));
  }

  [Fact]
  public void UnitState_RejectsIncompatibleOrder()
  {
    Assert.Throws<ArgumentException>(() => new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1, 1),
        MovementOrder.Task(new OrderId(1), UnitOrderKind.Attack, new EntityId(9)), default, MapRevision.Initial));
  }

  [Fact]
  public void UnitState_IdleHasNoRouteNorRepath()
  {
    Assert.Throws<ArgumentException>(() => new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1, 1),
        MovementOrder.Idle, [new PositionXZ(2, 2)], MapRevision.Initial));
    Assert.Throws<ArgumentException>(() => new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1, 1),
        MovementOrder.Idle, default, MapRevision.Initial, repathPendingSinceTick: new TickId(3)));
  }

  [Fact]
  public void UnitState_RepathPendingFlag()
  {
    var state = new UnitMovementState(
        new EntityId(1), PlayerId.One, UnitType.Soldier, new PositionXZ(1, 1),
        MovementOrder.Move(new OrderId(4), new PositionXZ(9, 9)), [new PositionXZ(9, 9)], MapRevision.Initial,
        repathPendingSinceTick: new TickId(10));

    Assert.True(state.IsRepathPending);
  }

  [Fact]
  public void MovementTarget_IsPointOrFootprint()
  {
    MovementTarget tree = MovementTarget.AtPoint(new EntityId(50), new PositionXZ(4, 4));
    MovementTarget farm = MovementTarget.WithFootprint(new EntityId(60), new Footprint(new GridCell(2, 2), new FootprintSize(2, 2)));

    Assert.NotNull(tree.Point);
    Assert.Null(tree.Footprint);
    Assert.NotNull(farm.Footprint);
    Assert.Null(farm.Point);
    Assert.Throws<ArgumentException>(() => MovementTarget.AtPoint(EntityId.None, new PositionXZ(1, 1)));
  }

  [Fact]
  public void NavigationGrid_OutsideCellsAreBlocked()
  {
    var map = new NavigationGrid(Grid, new MapRevision(3), [new GridCell(5, 5)]);

    Assert.True(map.IsBlocked(new GridCell(5, 5)));
    Assert.False(map.IsBlocked(new GridCell(4, 5)));
    Assert.True(map.IsBlocked(new GridCell(-1, 0)));
    Assert.True(map.IsBlocked(new GridCell(20, 0)));
    Assert.Throws<ArgumentException>(() => new NavigationGrid(Grid, MapRevision.Initial, [new GridCell(25, 0)]));
  }

  [Fact]
  public void NavigationGrid_HypotheticalFootprintDoesNotMutateOriginal()
  {
    var map = new NavigationGrid(Grid, new MapRevision(3), []);
    var footprint = new Footprint(new GridCell(2, 2), new FootprintSize(2, 1));

    NavigationGrid hypothetical = map.WithBlockedFootprint(footprint);

    Assert.True(hypothetical.IsBlocked(new GridCell(3, 2)));
    Assert.False(map.IsBlocked(new GridCell(3, 2)));
    Assert.Equal(map.Revision, hypothetical.Revision);
    Assert.Throws<ArgumentException>(() =>
        map.WithBlockedFootprint(new Footprint(new GridCell(19, 19), new FootprintSize(2, 2))));
  }
}
