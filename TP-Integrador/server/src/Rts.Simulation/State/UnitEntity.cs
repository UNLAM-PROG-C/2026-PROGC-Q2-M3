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

  /// <summary>
  /// Copia de la unidad sin orden (Idle): sin ruta, sin repath pendiente y con el contador de recolección descartado (01 §5).
  /// Conserva posición, HP y cooldown: cancelar una orden no revierte progreso ni recursos (04 §3).
  /// </summary>
  internal UnitEntity AsIdle() => new(
      Id, Owner, Type, Position, Hp, MovementOrder.Idle, route: EquatableArray<PositionXZ>.Empty, RouteMapRevision,
      repathPendingSinceTick: null, lastPursuitRepathTick: null, gatherTicksAccumulated: 0, ReadyAtTick);

  /// <summary>
  /// Copia de la unidad con una orden nueva y su ruta validada contra <paramref name="routeMapRevision"/> (04 §3).
  /// La orden anterior se descarta entera: sin repath pendiente, sin marca de persecución y sin contador de recolección.
  /// </summary>
  internal UnitEntity WithNewOrder(MovementOrder order, EquatableArray<PositionXZ> route, MapRevision routeMapRevision) => new(
      Id, Owner, Type, Position, Hp, order, route, routeMapRevision,
      repathPendingSinceTick: null, lastPursuitRepathTick: null, gatherTicksAccumulated: 0, ReadyAtTick);

  /// <summary>
  /// Copia de la unidad con la parte de Movement reemplazada por la que propuso la fase 2 (posición, orden, ruta, repath).
  /// HP y cooldown no cambian. El contador de recolección solo se conserva si la orden sigue siendo la misma (mismo OrderId).
  /// </summary>
  /// <exception cref="InvalidOperationException">Si la propuesta es de otra unidad o le cambia el dueño o el tipo.</exception>
  internal UnitEntity WithMovement(UnitMovementState movement)
  {
    if (movement.UnitId != Id || movement.Owner != Owner || movement.Type != Type)
    {
      throw new InvalidOperationException($"Movement propuso un estado incoherente para la unidad {Id}.");
    }

    int gatherTicks = movement.Order.OrderId == Order.OrderId ? GatherTicksAccumulated : 0;
    return new UnitEntity(
        Id, Owner, Type, movement.Position, Hp, movement.Order, movement.RemainingWaypoints, movement.RouteMapRevision,
        movement.RepathPendingSinceTick, movement.LastPursuitRepathTick, gatherTicks, ReadyAtTick);
  }

  /// <summary>Copia con el contador de recolección que calculó Economy. Solo vale con una orden GatherWood (lo verifica el constructor).</summary>
  internal UnitEntity WithGatherTicks(int gatherTicks) => new(
      Id, Owner, Type, Position, Hp, Order, Route, RouteMapRevision,
      RepathPendingSinceTick, LastPursuitRepathTick, gatherTicks, ReadyAtTick);

  /// <summary>La parte de la unidad que ve Movement.</summary>
  internal UnitMovementState ToMovementState() => new(
      Id, Owner, Type, Position, Order, Route, RouteMapRevision, RepathPendingSinceTick, LastPursuitRepathTick);
}
