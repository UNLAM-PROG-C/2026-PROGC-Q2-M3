using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Shared.Events;

// Payloads de eventos de producción (D-009).

/// <summary>
/// Apareció una unidad nueva (05 §5). Lo emite Simulation al confirmar el spawn, porque asigna el <see cref="UnitId"/>.
/// Visible para ambos jugadores; siempre precede a la primera posición publicada de esa unidad (09 §5).
/// </summary>
public sealed record UnitSpawned(
    EntityId UnitId,
    PlayerId Owner,
    UnitType UnitType,
    PositionXZ Position,
    int Hp) : IDomainEventPayload;

/// <summary>
/// Cambió el estado discreto de un productor: solicitud encolada, inicio, aparición, espera por comida o por celda.
/// Visible **solo para el dueño** (09 §4): nunca se exponen colas rivales. No se emite en cada tick de progreso:
/// <see cref="RemainingTicks"/> junto con el tick del evento permite a Godot mostrar el avance (D-042).
/// Lleva <see cref="Owner"/> para que State Sync lo dirija sin mantener estado (D-063).
/// </summary>
public sealed record ProductionChanged(
    EntityId ProducerId,
    PlayerId Owner,
    ProducerStatus Status,
    UnitType? ActiveUnitType,
    int? RemainingTicks,
    EquatableArray<UnitType> PendingUnitTypes) : IDomainEventPayload;
