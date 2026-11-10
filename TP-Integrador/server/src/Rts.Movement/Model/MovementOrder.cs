using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement.Model;

/// <summary>
/// Orden activa de una unidad: la "intención vigente" que se conserva en un repath (04 §5).
/// <list type="bullet">
/// <item><see cref="UnitOrderKind.Idle"/>: sin <see cref="OrderId"/>, destino ni objetivo.</item>
/// <item><see cref="UnitOrderKind.Move"/>: con destino.</item>
/// <item>Órdenes de tarea (GatherWood, Build, RepairFarm, Attack): con objetivo.</item>
/// </list>
/// El <see cref="OrderId"/> lo asigna Simulation al aceptar la orden (04 §4).
/// </summary>
public sealed record MovementOrder
{
  public static readonly MovementOrder Idle = new(OrderId.None, UnitOrderKind.Idle, null, null);

  private MovementOrder(OrderId orderId, UnitOrderKind kind, PositionXZ? destination, EntityId? targetId)
  {
    OrderId = orderId;
    Kind = kind;
    Destination = destination;
    TargetId = targetId;
  }

  public OrderId OrderId { get; }

  public UnitOrderKind Kind { get; }

  public PositionXZ? Destination { get; }

  public EntityId? TargetId { get; }

  public bool IsIdle => Kind == UnitOrderKind.Idle;

  public static MovementOrder Move(OrderId orderId, PositionXZ destination)
  {
    EnsureOrderId(orderId);
    if (!destination.IsFinite)
    {
      throw new ArgumentException("El destino debe ser finito.", nameof(destination));
    }

    return new MovementOrder(orderId, UnitOrderKind.Move, destination, null);
  }

  public static MovementOrder Task(OrderId orderId, UnitOrderKind kind, EntityId targetId)
  {
    EnsureOrderId(orderId);
    if (!UnitOrderCompatibility.IsTaskOrder(kind))
    {
      throw new ArgumentException($"{kind} no es una orden de tarea.", nameof(kind));
    }

    if (targetId.IsNone)
    {
      throw new ArgumentException("Una orden de tarea requiere objetivo.", nameof(targetId));
    }

    return new MovementOrder(orderId, kind, null, targetId);
  }

  private static void EnsureOrderId(OrderId orderId)
  {
    if (orderId.IsNone)
    {
      throw new ArgumentException("Una orden activa requiere OrderId asignado por el servidor.", nameof(orderId));
    }
  }
}
