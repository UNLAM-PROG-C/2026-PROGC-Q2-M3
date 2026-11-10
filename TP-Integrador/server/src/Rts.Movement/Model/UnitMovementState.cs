using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement.Model;

/// <summary>
/// Vista de movimiento inmutable de una unidad: entrada del tick y estado propuesto en el resultado.
/// <list type="bullet">
/// <item><see cref="Position"/>: posición oficial X/Z (autoridad del servidor).</item>
/// <item><see cref="RemainingWaypoints"/>: tramo validado que falta recorrer; vacío si no se mueve.</item>
/// <item><see cref="RouteMapRevision"/>: revisión de mapa contra la que se validó la ruta restante.</item>
/// <item><see cref="RepathPendingSinceTick"/>: no nulo ⇒ <c>RepathPending</c>: la unidad está pausada esperando ruta (04 §5).</item>
/// <item><see cref="LastPursuitRepathTick"/>: último pedido de ruta por persecución, para limitar su frecuencia (D-027).</item>
/// </list>
/// Invariantes: orden compatible con el tipo de unidad; una unidad Idle no tiene ruta ni repath pendiente.
/// </summary>
public sealed record UnitMovementState
{
    public UnitMovementState(
        EntityId unitId,
        PlayerId owner,
        UnitType type,
        PositionXZ position,
        MovementOrder order,
        EquatableArray<PositionXZ> remainingWaypoints,
        MapRevision routeMapRevision,
        TickId? repathPendingSinceTick = null,
        TickId? lastPursuitRepathTick = null)
    {
        if (unitId.IsNone)
        {
            throw new ArgumentException("La unidad debe tener un EntityId válido.", nameof(unitId));
        }

        if (!owner.IsValid)
        {
            throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentException($"Tipo de unidad inválido: {type}.", nameof(type));
        }

        if (!position.IsFinite || remainingWaypoints.Any(w => !w.IsFinite))
        {
            throw new ArgumentException("Posición y waypoints deben ser finitos.", nameof(position));
        }

        ArgumentNullException.ThrowIfNull(order);
        if (!UnitOrderCompatibility.IsAllowed(type, order.Kind))
        {
            throw new ArgumentException($"Un {type} no admite la orden {order.Kind}.", nameof(order));
        }

        if (order.IsIdle && (!remainingWaypoints.IsEmpty || repathPendingSinceTick is not null))
        {
            throw new ArgumentException("Una unidad Idle no tiene ruta ni repath pendiente.", nameof(order));
        }

        UnitId = unitId;
        Owner = owner;
        Type = type;
        Position = position;
        Order = order;
        RemainingWaypoints = remainingWaypoints;
        RouteMapRevision = routeMapRevision;
        RepathPendingSinceTick = repathPendingSinceTick;
        LastPursuitRepathTick = lastPursuitRepathTick;
    }

    public EntityId UnitId { get; }

    public PlayerId Owner { get; }

    public UnitType Type { get; }

    public PositionXZ Position { get; }

    public MovementOrder Order { get; }

    public EquatableArray<PositionXZ> RemainingWaypoints { get; }

    public MapRevision RouteMapRevision { get; }

    public TickId? RepathPendingSinceTick { get; }

    public TickId? LastPursuitRepathTick { get; }

    public bool IsRepathPending => RepathPendingSinceTick is not null;
}
