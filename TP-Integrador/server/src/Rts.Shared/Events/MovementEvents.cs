using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Shared.Events;

// Payloads de eventos de órdenes y rutas (D-009). Las posiciones oficiales NO son eventos:
// State Sync las publica periódicamente a 10 Hz desde el estado confirmado (09 §5).

/// <summary>
/// La orden activa de una unidad cambió (nueva orden aceptada, llegada de Move, Stop, cancelación, objetivo perdido).
/// <see cref="OrderId"/> es <c>None</c> cuando pasa a <see cref="UnitOrderKind.Idle"/>.
/// </summary>
public sealed record UnitOrderChanged(EntityId UnitId, OrderId OrderId, UnitOrderKind Kind) : IDomainEventPayload;

/// <summary>
/// El servidor pide a Godot una ruta nueva para la misma intención vigente (04 §5). Visibilidad: solo el dueño (D-028);
/// lleva <see cref="Owner"/> para que State Sync lo dirija sin mantener estado (D-063).
/// <para>
/// <see cref="IsPaused"/> es verdadero si la ruta quedó invalidada por un cambio de mapa: la unidad está en
/// <c>RepathPending</c> hasta recibir una ruta válida o vencer el timeout. Es falso en una persecución con el camino
/// directo bloqueado: la unidad sigue su ruta vigente mientras espera.
/// </para>
/// Exactamente uno de <see cref="Destination"/> o <see cref="TargetId"/> está presente.
/// </summary>
public sealed record RepathRequested : IDomainEventPayload
{
  public RepathRequested(
      EntityId unitId,
      PlayerId owner,
      OrderId orderId,
      MapRevision mapRevision,
      PositionXZ? destination,
      EntityId? targetId,
      bool isPaused)
  {
    if (unitId.IsNone || orderId.IsNone || !owner.IsValid)
    {
      throw new ArgumentException("Se requieren una unidad, un dueño y una orden válidos.");
    }

    if ((destination is null) == (targetId is null))
    {
      throw new ArgumentException("Debe indicarse exactamente uno: destino u objetivo.");
    }

    UnitId = unitId;
    Owner = owner;
    OrderId = orderId;
    MapRevision = mapRevision;
    Destination = destination;
    TargetId = targetId;
    IsPaused = isPaused;
  }

  public EntityId UnitId { get; }

  /// <summary>Dueño de la unidad: único destinatario del evento.</summary>
  public PlayerId Owner { get; }

  public OrderId OrderId { get; }

  public MapRevision MapRevision { get; }

  public PositionXZ? Destination { get; }

  public EntityId? TargetId { get; }

  public bool IsPaused { get; }
}
