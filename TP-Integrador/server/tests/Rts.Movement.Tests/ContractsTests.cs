using Rts.Movement.Configuration;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement.Tests;

public class ContractsTests
{
    private static readonly NavigationGrid Map = new(new GridSpec(1.0, 20, 20), new MapRevision(2), []);

    private static readonly UnitMovementState Worker = new(
        new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1, 1), MovementOrder.Idle, default, MapRevision.Initial);

    /// <summary>Configuración válida SOLO para pruebas; los números son suposiciones temporales, no balance aprobado.</summary>
    internal static MovementConfig ValidConfig() => new()
    {
        SpeedPerSecond = new Dictionary<UnitType, double> { [UnitType.Worker] = 3.0, [UnitType.Soldier] = 4.0 },
        ArrivalTolerance = 0.25,
        GatherRange = 1.0,
        BuildRange = 1.0,
        AttackRange = 1.5,
        MaxWaypointsPerRoute = 64,
        RepathTimeoutTicks = 40,
        PursuitRepathIntervalTicks = 10,
    };

    [Fact]
    public void Config_ValidHasNoErrors()
    {
        Assert.Empty(ValidConfig().GetValidationErrors());
    }

    [Fact]
    public void Config_RejectsNonPositiveValues()
    {
        MovementConfig config = ValidConfig() with
        {
            SpeedPerSecond = new Dictionary<UnitType, double> { [UnitType.Worker] = 0 },
            GatherRange = double.NaN,
            RepathTimeoutTicks = 0,
        };

        IReadOnlyList<string> errors = config.GetValidationErrors();

        Assert.Equal(4, errors.Count);
        Assert.Throws<ArgumentException>(config.Validate);
    }

    [Fact]
    public void TickInput_RejectsDuplicateUnits()
    {
        Assert.Throws<ArgumentException>(() => new MovementTickInput(TickId.Zero, Map, [Worker, Worker], default));
    }

    [Fact]
    public void TickResult_EqualContentIsEqual()
    {
        MovementTickResult Build() => MovementTickResult.Empty(new TickId(5)) with
        {
            UnitUpdates = [Worker],
            OrdersEnded = [new OrderEnded(Worker.UnitId, new OrderId(3), OrderEndReason.Completed)],
            ProposedEvents = [new UnitOrderChanged(Worker.UnitId, OrderId.None, UnitOrderKind.Idle)],
        };

        Assert.Equal(Build(), Build());
    }

    [Fact]
    public void OrderRequest_ShapeMustMatchKind()
    {
        MovementTarget tree = MovementTarget.AtPoint(new EntityId(50), new PositionXZ(4, 4));

        _ = new UnitOrderRequest(Worker, UnitOrderKind.Move, new PositionXZ(3, 3), null, [new PositionXZ(3, 3)], Map);
        _ = new UnitOrderRequest(Worker, UnitOrderKind.GatherWood, null, tree, [new PositionXZ(3, 4)], Map);
        _ = new UnitOrderRequest(Worker, UnitOrderKind.Idle, null, null, default, Map);

        Assert.Throws<ArgumentException>(() => new UnitOrderRequest(Worker, UnitOrderKind.Move, null, null, default, Map));
        Assert.Throws<ArgumentException>(() => new UnitOrderRequest(Worker, UnitOrderKind.GatherWood, new PositionXZ(1, 1), tree, default, Map));
        Assert.Throws<ArgumentException>(() => new UnitOrderRequest(Worker, UnitOrderKind.Idle, null, null, [new PositionXZ(1, 1)], Map));
    }

    [Fact]
    public void Evaluations_RequireErrorWhenRejected()
    {
        UnitOrderEvaluation accepted = UnitOrderEvaluation.Accepted(Worker.UnitId, [new PositionXZ(3, 3)]);
        UnitOrderEvaluation rejected = UnitOrderEvaluation.Rejected(Worker.UnitId, MovementErrors.InvalidPath);

        Assert.True(accepted.IsAccepted);
        Assert.False(rejected.IsAccepted);
        Assert.True(rejected.AcceptedRoute.IsEmpty);
        Assert.Throws<ArgumentException>(() => UnitOrderEvaluation.Rejected(Worker.UnitId, default));
    }

    [Fact]
    public void RouteValidation_InvalidCarriesErrorAndSegment()
    {
        RouteValidation invalid = RouteValidation.Invalid(MovementErrors.InvalidPath, failedSegmentIndex: 2);

        Assert.True(RouteValidation.Valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.Equal(2, invalid.FailedSegmentIndex);
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteValidation.Invalid(MovementErrors.InvalidPath, -1));
    }

    [Fact]
    public void MovementErrors_UseMovementArea()
    {
        Assert.All(
            [MovementErrors.InvalidPath, MovementErrors.TooManyWaypoints, MovementErrors.RouteDoesNotReachGoal,
             MovementErrors.DestinationOutOfBounds, MovementErrors.StaleOrder, MovementErrors.Unreachable],
            code => Assert.Equal("Movement", code.Area));
    }
}
