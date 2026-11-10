using Rts.Shared.Ids;

namespace Rts.Shared.Events;

// Payloads de eventos propuestos por Combat (D-009). Simulation los sella como DomainEvent al confirmar;
// State Sync los proyecta a DTOs. Visibles para ambos jugadores (09 §4).

/// <summary>
/// Una entidad (unidad o edificio, incluso en obra) recibió daño y sobrevivió. <see cref="Hp"/> es el HP resultante.
/// Para obras, State Sync decide si lo publica como daño o como cambio de progreso (O-009).
/// </summary>
public sealed record EntityDamaged(EntityId EntityId, int Hp) : IDomainEventPayload;

/// <summary>
/// Una entidad llegó a 0 HP y se destruye. Al confirmarlo, Simulation libera la huella (si es edificio),
/// cambia la <c>MapRevision</c> y cancela las operaciones que dependían de ella.
/// </summary>
public sealed record EntityDestroyed(EntityId EntityId) : IDomainEventPayload;
