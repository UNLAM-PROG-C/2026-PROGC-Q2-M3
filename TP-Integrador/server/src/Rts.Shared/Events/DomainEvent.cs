namespace Rts.Shared.Events;

/// <summary>
/// Evento de dominio confirmado (06 §8-9). Solo Simulation lo crea, y únicamente después del commit;
/// los módulos de dominio proponen payloads sin sello. State Sync lo transforma en DTOs por destinatario.
/// </summary>
public sealed record DomainEvent(EventStamp Stamp, IDomainEventPayload Payload);
