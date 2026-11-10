using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement.Contracts;

/// <summary>
/// Pedido de evaluación de una orden para una unidad individual (04 §7, <c>EvaluateMoveOrder</c>), en la fase 1.
/// Simulation ya verificó la propiedad de la unidad y las condiciones del dueño del objetivo (Combat para Attack,
/// Economy para el árbol, Construction para Build/RepairFarm). Movement evalúa compatibilidad, ruta y llegada.
/// <list type="bullet">
/// <item>Move: <see cref="Destination"/> presente, <see cref="Target"/> nulo.</item>
/// <item>Órdenes de tarea: <see cref="Target"/> presente, <see cref="Destination"/> nulo.</item>
/// <item>Idle (Stop): sin destino, objetivo ni waypoints.</item>
/// </list>
/// </summary>
public sealed record UnitOrderRequest
{
    public UnitOrderRequest(
        UnitMovementState unit,
        UnitOrderKind kind,
        PositionXZ? destination,
        MovementTarget? target,
        EquatableArray<PositionXZ> waypoints,
        NavigationGrid map)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(map);

        bool shapeIsValid = kind switch
        {
            UnitOrderKind.Idle => destination is null && target is null && waypoints.IsEmpty,
            UnitOrderKind.Move => destination is not null && target is null,
            _ when UnitOrderCompatibility.IsTaskOrder(kind) => destination is null && target is not null,
            _ => false,
        };

        if (!shapeIsValid)
        {
            throw new ArgumentException($"Pedido de orden mal formado para {kind}.");
        }

        Unit = unit;
        Kind = kind;
        Destination = destination;
        Target = target;
        Waypoints = waypoints;
        Map = map;
    }

    public UnitMovementState Unit { get; }

    public UnitOrderKind Kind { get; }

    public PositionXZ? Destination { get; }

    public MovementTarget? Target { get; }

    public EquatableArray<PositionXZ> Waypoints { get; }

    public NavigationGrid Map { get; }
}

/// <summary>
/// Resultado de evaluar una orden o un RepathResponse para una unidad. Si se acepta, <see cref="AcceptedRoute"/> es la ruta
/// validada que Simulation instala en la unidad, junto con un OrderId nuevo (orden) o el mismo (repath).
/// Si se rechaza, la orden anterior de la unidad se conserva sin cambios (04 §2).
/// </summary>
public sealed record UnitOrderEvaluation
{
    private UnitOrderEvaluation(EntityId unitId, ErrorCode? error, EquatableArray<PositionXZ> acceptedRoute)
    {
        UnitId = unitId;
        Error = error;
        AcceptedRoute = acceptedRoute;
    }

    public EntityId UnitId { get; }

    public bool IsAccepted => Error is null;

    public ErrorCode? Error { get; }

    public EquatableArray<PositionXZ> AcceptedRoute { get; }

    public static UnitOrderEvaluation Accepted(EntityId unitId, EquatableArray<PositionXZ> route) => new(unitId, null, route);

    public static UnitOrderEvaluation Rejected(EntityId unitId, ErrorCode error)
    {
        if (error.IsDefault)
        {
            throw new ArgumentException("El rechazo requiere código de error.", nameof(error));
        }

        return new UnitOrderEvaluation(unitId, error, default);
    }
}
