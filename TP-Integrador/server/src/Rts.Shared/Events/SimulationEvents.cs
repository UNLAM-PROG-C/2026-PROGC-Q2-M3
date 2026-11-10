using Rts.Shared.Ids;
using Rts.Shared.Resources;

namespace Rts.Shared.Events;

/// <summary>
/// Saldo confirmado de un jugador al cierre del tick, si cambió durante el tick (D-048). Lo emite Simulation una vez por
/// jugador y tick, después de Production. **Privado:** solo para el dueño (09 §4).
/// </summary>
public sealed record ResourcesChanged(PlayerId Player, ResourceAmount Balance) : IDomainEventPayload;
