using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Simulation.State;

/// <summary>
/// Unidad en el <see cref="WorldState"/> oficial (inmutable, D-044). Reúne los datos que los módulos ven por partes:
/// <list type="bullet">
/// <item>Movement: posición, orden, ruta, revisión de la ruta, repath.</item>
/// <item>Economy: <see cref="GatherTicksAccumulated"/> (contador de recolección de la orden vigente; 0 si no recolecta).</item>
/// <item>Combat: <see cref="Hp"/> y <see cref="ReadyAtTick"/> (cooldown del soldado; sin uso en trabajadores).</item>
/// </list>
/// </summary>
public sealed record UnitEntity
{
    public UnitEntity(
        EntityId id,
        PlayerId owner,
        UnitType type,
        PositionXZ position,
        int hp,
        MovementOrder order,
        EquatableArray<PositionXZ> route,
        MapRevision routeMapRevision,
        TickId? repathPendingSinceTick,
        TickId? lastPursuitRepathTick,
        int gatherTicksAccumulated,
        TickId readyAtTick)
    {
        if (id.IsNone || !owner.IsValid || !Enum.IsDefined(type))
        {
            throw new ArgumentException("Unidad con ID, dueño o tipo inválido.");
        }

        if (!position.IsFinite)
        {
            throw new ArgumentException("La posición debe ser finita.", nameof(position));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hp);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentOutOfRangeException.ThrowIfNegative(gatherTicksAccumulated);

        if (!UnitOrderCompatibility.IsAllowed(type, order.Kind))
        {
            throw new ArgumentException($"Un {type} no admite la orden {order.Kind}.", nameof(order));
        }

        if (order.IsIdle && (!route.IsEmpty || repathPendingSinceTick is not null || gatherTicksAccumulated != 0))
        {
            throw new ArgumentException("Una unidad Idle no tiene ruta, repath pendiente ni contador de recolección.", nameof(order));
        }

        if (gatherTicksAccumulated != 0 && order.Kind != UnitOrderKind.GatherWood)
        {
            throw new ArgumentException("Solo una orden GatherWood acumula contador de recolección.", nameof(gatherTicksAccumulated));
        }

        Id = id;
        Owner = owner;
        Type = type;
        Position = position;
        Hp = hp;
        Order = order;
        Route = route;
        RouteMapRevision = routeMapRevision;
        RepathPendingSinceTick = repathPendingSinceTick;
        LastPursuitRepathTick = lastPursuitRepathTick;
        GatherTicksAccumulated = gatherTicksAccumulated;
        ReadyAtTick = readyAtTick;
    }

    public EntityId Id { get; }

    public PlayerId Owner { get; }

    public UnitType Type { get; }

    public PositionXZ Position { get; }

    public int Hp { get; }

    public MovementOrder Order { get; }

    public EquatableArray<PositionXZ> Route { get; }

    public MapRevision RouteMapRevision { get; }

    public TickId? RepathPendingSinceTick { get; }

    public TickId? LastPursuitRepathTick { get; }

    public int GatherTicksAccumulated { get; }

    public TickId ReadyAtTick { get; }
}
