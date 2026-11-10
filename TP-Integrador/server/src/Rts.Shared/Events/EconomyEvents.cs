using Rts.Shared.Ids;

namespace Rts.Shared.Events;

// Payloads de eventos propuestos por Economy (D-009). Simulation los sella como DomainEvent al confirmar;
// State Sync los proyecta a DTOs. Visibles para ambos jugadores (09 §4).
// ResourcesChanged (solo dueño) no está aquí: Simulation lo deriva de los saldos confirmados, no de Economy.

/// <summary>Un árbol se agotó: queda invisible y no recolectable; conserva ID, posición y celdas reservadas (01 §3).</summary>
public sealed record TreeDepleted(EntityId TreeId) : IDomainEventPayload;

/// <summary>Un árbol se regeneró con su madera máxima y vuelve a ser visible; recolectable desde el tick siguiente (01 §3).</summary>
public sealed record TreeRespawned(EntityId TreeId) : IDomainEventPayload;

/// <summary>Una granja consumió sus tandas: queda agotada pero no destruida (01 §4).</summary>
public sealed record FarmDepleted(EntityId FarmId) : IDomainEventPayload;
