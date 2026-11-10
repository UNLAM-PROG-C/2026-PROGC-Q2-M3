using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Movement.Contracts;

/// <summary>
/// Propuesta inmutable de la fase 2 (04 §7). Simulation la confirma de inmediato, antes de Construction.
/// Las colecciones vienen ordenadas por UnitId ascendente.
/// </summary>
/// <param name="Tick">Tick de la entrada.</param>
/// <param name="UnitUpdates">Nuevo estado completo de cada unidad que cambió (posición, ruta, orden, repath).</param>
/// <param name="OrdersEnded">Órdenes que Movement terminó en este tick, con su motivo; esas unidades quedan Idle en <paramref name="UnitUpdates"/>.</param>
/// <param name="ProposedEvents">Eventos propuestos (<c>UnitOrderChanged</c>, <c>RepathRequested</c>) por UnitId ascendente.</param>
public sealed record MovementTickResult(
    TickId Tick,
    EquatableArray<UnitMovementState> UnitUpdates,
    EquatableArray<OrderEnded> OrdersEnded,
    EquatableArray<IDomainEventPayload> ProposedEvents)
{
    public static MovementTickResult Empty(TickId tick) => new(tick, default, default, default);
}

/// <summary>Orden terminada por Movement.</summary>
public sealed record OrderEnded(EntityId UnitId, OrderId OrderId, OrderEndReason Reason);
