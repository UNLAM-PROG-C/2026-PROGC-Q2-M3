using Rts.Shared.Events;

namespace Rts.StateSync.Visibility;

/// <summary>
/// Tabla de visibilidad del contrato 09 §3. Decide solo con el evento, sin estado. Por ahora conoce los eventos de la fase 1;
/// el resto se agrega en la fase 2 y, mientras tanto, falla.
/// </summary>
public sealed class EventAudiencePolicy : IEventAudiencePolicy
{
  public EventAudience GetAudience(IDomainEventPayload payload)
  {
    ArgumentNullException.ThrowIfNull(payload);

    return payload switch
    {
      UnitOrderChanged => EventAudience.AllPlayers,
      _ => throw new NotSupportedException($"Evento sin visibilidad definida: {payload.GetType().Name}."),
    };
  }
}
