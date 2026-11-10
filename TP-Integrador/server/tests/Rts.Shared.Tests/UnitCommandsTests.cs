using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Shared.Tests;

public class UnitCommandsTests
{
    private static readonly UnitRouteCandidate RouteA = new(new EntityId(1), [new PositionXZ(2, 2), new PositionXZ(5, 3)]);
    private static readonly UnitRouteCandidate RouteB = new(new EntityId(2), default);

    [Fact]
    public void MoveCommand_AcceptsOneRoutePerUnit()
    {
        var command = new MoveCommand(new PositionXZ(5, 3), new MapRevision(4), [RouteA, RouteB]);

        Assert.Equal(CommandType.Move, command.Type);
        Assert.Equal(2, command.Routes.Count);
    }

    [Fact]
    public void UnitCommands_RejectEmptyOrRepeatedUnits()
    {
        Assert.Throws<ArgumentException>(() => new MoveCommand(new PositionXZ(1, 1), MapRevision.Initial, default));
        Assert.Throws<ArgumentException>(() => new AttackCommand(new EntityId(9), MapRevision.Initial, [RouteA, RouteA]));
        Assert.Throws<ArgumentException>(() => new StopCommand(default));
        Assert.Throws<ArgumentException>(() => new StopCommand([new EntityId(1), new EntityId(1)]));
    }

    [Fact]
    public void UnitCommands_RejectNonFiniteCoordinatesAndMissingTargets()
    {
        Assert.Throws<ArgumentException>(() => new MoveCommand(new PositionXZ(double.NaN, 1), MapRevision.Initial, [RouteA]));
        Assert.Throws<ArgumentException>(() => new UnitRouteCandidate(new EntityId(1), [new PositionXZ(1, double.PositiveInfinity)]));
        Assert.Throws<ArgumentException>(() => new GatherWoodCommand(EntityId.None, MapRevision.Initial, [RouteA]));
        Assert.Throws<ArgumentException>(() => new RepairFarmCommand(EntityId.None, MapRevision.Initial, [RouteA]));
    }

    [Fact]
    public void BuildCommand_PlaceRequiresDefinedBuildingType()
    {
        BuildCommand command = BuildCommand.Place(BuildingType.Tower, new GridCell(3, 4), MapRevision.Initial, [RouteA]);

        Assert.Equal(CommandType.Build, command.Type);
        Assert.True(command.IsPlacement);
        Assert.Equal(BuildingType.Tower, command.BuildingType);
        Assert.Null(command.ExistingBuildingId);
        Assert.Throws<ArgumentException>(() => BuildCommand.Place((BuildingType)99, new GridCell(3, 4), MapRevision.Initial, [RouteA]));
    }

    [Fact]
    public void BuildCommand_ContinueTargetsExistingBuilding()
    {
        BuildCommand command = BuildCommand.Continue(new EntityId(40), MapRevision.Initial, [RouteA]);

        Assert.False(command.IsPlacement);
        Assert.Equal(new EntityId(40), command.ExistingBuildingId);
        Assert.Null(command.BuildingType);
        Assert.Null(command.Anchor);
        Assert.Throws<ArgumentException>(() => BuildCommand.Continue(EntityId.None, MapRevision.Initial, [RouteA]));
    }

    [Fact]
    public void TrainUnit_RequiresProducerAndDefinedUnitType()
    {
        var command = new TrainUnitCommand(new EntityId(3), UnitType.Worker);

        Assert.Equal(CommandType.TrainUnit, command.Type);
        Assert.Throws<ArgumentException>(() => new TrainUnitCommand(EntityId.None, UnitType.Worker));
        Assert.Throws<ArgumentException>(() => new TrainUnitCommand(new EntityId(3), (UnitType)42));
    }

    [Fact]
    public void RepathResponse_RequiresUnitAndOrder()
    {
        var command = new RepathResponseCommand(new EntityId(1), new OrderId(7), new MapRevision(2), [new PositionXZ(1, 1)]);

        Assert.Equal(CommandType.RepathResponse, command.Type);
        Assert.Throws<ArgumentException>(() => new RepathResponseCommand(new EntityId(1), OrderId.None, MapRevision.Initial, default));
    }

    [Fact]
    public void Commands_HaveStructuralEquality()
    {
        Assert.Equal(
            new MoveCommand(new PositionXZ(5, 3), new MapRevision(4), [RouteA]),
            new MoveCommand(new PositionXZ(5, 3), new MapRevision(4), [new UnitRouteCandidate(new EntityId(1), [new PositionXZ(2, 2), new PositionXZ(5, 3)])]));
    }

    [Fact]
    public void RepathRequested_RequiresExactlyOneGoal()
    {
        var byTarget = new RepathRequested(new EntityId(1), PlayerId.One, new OrderId(3), MapRevision.Initial, null, new EntityId(9), isPaused: false);

        Assert.Equal(new EntityId(9), byTarget.TargetId);
        Assert.Throws<ArgumentException>(() =>
            new RepathRequested(new EntityId(1), PlayerId.One, new OrderId(3), MapRevision.Initial, null, null, true));
        Assert.Throws<ArgumentException>(() =>
            new RepathRequested(new EntityId(1), PlayerId.One, new OrderId(3), MapRevision.Initial, new PositionXZ(1, 1), new EntityId(9), true));
    }
}
