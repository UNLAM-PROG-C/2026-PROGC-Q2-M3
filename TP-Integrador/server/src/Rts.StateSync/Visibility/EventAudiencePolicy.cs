using Rts.Shared.Events;

namespace Rts.StateSync.Visibility;

/// <summary>
/// Tabla de visibilidad del contrato 09 §3. Decide solo con el evento, sin estado: los eventos privados traen su dueño (D-063).
/// Un evento que no está en la tabla hace fallar la política, para que nunca se publique por descuido.
/// </summary>
public sealed class EventAudiencePolicy : IEventAudiencePolicy
{
  public EventAudience GetAudience(IDomainEventPayload payload)
  {
    ArgumentNullException.ThrowIfNull(payload);

    return payload switch
    {
      BuildingPlaced or BuildingProgressChanged or BuildingCompleted
          or FarmDepleted or FarmRepairStarted or FarmRepairProgressChanged or FarmRepairCompleted
          or MapRevisionChanged
          or EntityDamaged or EntityDestroyed
          or TreeDepleted or TreeRespawned
          or UnitSpawned or UnitOrderChanged => EventAudience.AllPlayers,
      RepathRequested repath => EventAudience.Only(repath.Owner),
      ProductionChanged production => EventAudience.Only(production.Owner),
      ResourcesChanged resources => EventAudience.Only(resources.Player),
      _ => throw new NotSupportedException($"Evento sin visibilidad definida: {payload.GetType().Name}."),
    };
  }
}
