using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.StateSync.Visibility;

namespace Rts.StateSync.Tests.Visibility;

public class EventAudiencePolicyTests
{
  /// <summary>Dueño de los eventos privados: P2, para que no coincida con el primer jugador por casualidad.</summary>
  private static readonly PlayerId Owner = PlayerId.Two;

  private static readonly EntityId Unit = new(3);
  private static readonly EntityId Building = new(10);
  private static readonly EntityId Tree = new(50);
  private const int AnyHp = 100;

  private readonly EventAudiencePolicy _policy = new();

  /// <summary>Eventos públicos de la tabla de §3: los ven ambos jugadores.</summary>
  public static TheoryData<IDomainEventPayload> PublicEvents => new()
  {
    new BuildingPlaced(Building, Owner, BuildingType.Farm, new Footprint(new GridCell(1, 1), new FootprintSize(2, 2)), AnyHp),
    new BuildingProgressChanged(Building, AnyHp),
    new BuildingCompleted(Building),
    new FarmDepleted(Building),
    new FarmRepairStarted(Building),
    new FarmRepairProgressChanged(Building, 1, 2),
    new FarmRepairCompleted(Building),
    new MapRevisionChanged(new MapRevision(1)),
    new EntityDamaged(Unit, AnyHp),
    new EntityDestroyed(Unit),
    new TreeDepleted(Tree),
    new TreeRespawned(Tree),
    new UnitSpawned(Unit, Owner, UnitType.Worker, default, AnyHp),
    new UnitOrderChanged(Unit, new OrderId(1), UnitOrderKind.Move),
  };

  /// <summary>Eventos privados de la tabla de §3: solo los ve <see cref="Owner"/>.</summary>
  public static TheoryData<IDomainEventPayload> OwnerOnlyEvents => new()
  {
    new RepathRequested(Unit, Owner, new OrderId(1), new MapRevision(1), new PositionXZ(1, 1), null, false),
    new ProductionChanged(Building, Owner, ProducerStatus.Idle, null, null, []),
    new ResourcesChanged(Owner, new ResourceAmount(1, 1)),
  };

  [Theory]
  [MemberData(nameof(PublicEvents))]
  public void PublicEvent_IsForBothPlayers(IDomainEventPayload payload)
  {
    Assert.True(_policy.GetAudience(payload).IsForAllPlayers);
  }

  [Theory]
  [MemberData(nameof(OwnerOnlyEvents))]
  public void PrivateEvent_IsOnlyForItsOwner(IDomainEventPayload payload)
  {
    EventAudience audience = _policy.GetAudience(payload);

    Assert.Equal(Owner, audience.OnlyPlayer);
    Assert.False(audience.Includes(PlayerId.One));
  }

  [Fact]
  public void UnknownEvent_Throws()
  {
    Assert.Throws<NotSupportedException>(() => _policy.GetAudience(new UnknownPayload()));
  }

  /// <summary>Evento que la política no conoce: simula uno agregado sin decidir su visibilidad.</summary>
  private sealed record UnknownPayload : IDomainEventPayload;
}
