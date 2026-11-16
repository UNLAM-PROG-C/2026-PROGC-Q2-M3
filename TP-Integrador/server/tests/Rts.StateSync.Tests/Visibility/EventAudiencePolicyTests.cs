using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.StateSync.Visibility;

namespace Rts.StateSync.Tests.Visibility;

public class EventAudiencePolicyTests
{
  private readonly EventAudiencePolicy _policy = new();

  [Fact]
  public void UnitOrderChanged_IsForBothPlayers()
  {
    EventAudience audience = _policy.GetAudience(new UnitOrderChanged(new EntityId(3), new OrderId(1), UnitOrderKind.Move));

    Assert.True(audience.IsForAllPlayers);
  }

  [Fact]
  public void UnknownEvent_Throws()
  {
    Assert.Throws<NotSupportedException>(() => _policy.GetAudience(new UnknownPayload()));
  }

  /// <summary>Evento que la política no conoce: simula uno agregado sin decidir su visibilidad.</summary>
  private sealed record UnknownPayload : IDomainEventPayload;
}
