using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Shared.Events;

// Payloads de eventos de construcción y mapa (D-009). Visibles para ambos jugadores (09 §4).

/// <summary>
/// Se colocó una obra: ocupa su huella desde ahora (03 §2). Lo emite Simulation al confirmar el Build,
/// porque es quien asigna el <see cref="BuildingId"/>. Siempre va acompañado de <see cref="MapRevisionChanged"/>.
/// </summary>
public sealed record BuildingPlaced(
    EntityId BuildingId,
    PlayerId Owner,
    BuildingType BuildingType,
    Footprint Footprint,
    int Hp) : IDomainEventPayload;

/// <summary>El HP de una obra aumentó por trabajo de construcción (en obra, el HP es también el progreso, 03 §3).</summary>
public sealed record BuildingProgressChanged(EntityId BuildingId, int Hp) : IDomainEventPayload;

/// <summary>Una obra alcanzó su HP máximo: queda Completed y es operativa desde el tick siguiente (03 §3).</summary>
public sealed record BuildingCompleted(EntityId BuildingId) : IDomainEventPayload;

/// <summary>Comenzó la reparación de capacidad de una granja agotada; la madera ya se pagó (03 §4).</summary>
public sealed record FarmRepairStarted(EntityId FarmId) : IDomainEventPayload;

/// <summary>Avanzó la reparación de capacidad de una granja.</summary>
public sealed record FarmRepairProgressChanged(EntityId FarmId, int WorkDone, int WorkRequired) : IDomainEventPayload;

/// <summary>Terminó la reparación: la granja recupera sus tandas y reinicia su temporizador (03 §4, D-014). No cura HP.</summary>
public sealed record FarmRepairCompleted(EntityId FarmId) : IDomainEventPayload;

/// <summary>
/// Cambió la revisión del mapa de obstáculos (colocación o destrucción de edificios o vallas). Lo emite Simulation
/// después del evento estructural correspondiente, para que Godot actualice su navegación (09 §6.4, §8; D-034).
/// </summary>
public sealed record MapRevisionChanged(MapRevision Revision) : IDomainEventPayload;
