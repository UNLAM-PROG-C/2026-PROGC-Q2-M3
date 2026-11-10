namespace Rts.Shared.Events;

/// <summary>
/// Contenido de un evento de dominio confirmado (p. ej. <c>UnitSpawned</c>, <c>EntityDestroyed</c>).
/// RECOMENDADO (contrato 00 §5): los payloads concretos se definen en <c>Rts.Shared.Events</c> al trabajar cada
/// módulo, como records inmutables. La visibilidad (ambos / solo dueño / solo emisor) la decide State Sync.
/// </summary>
public interface IDomainEventPayload
{
}
