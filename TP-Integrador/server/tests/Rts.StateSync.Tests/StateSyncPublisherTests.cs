using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.State;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;
using Rts.StateSync.Projection;

namespace Rts.StateSync.Tests;

public class StateSyncPublisherTests
{
  private static readonly StateSyncConfig Config = new();

  /// <summary>Primer tick con posiciones según la configuración por defecto.</summary>
  private static readonly TickId PositionTick = new(Config.GetPositionIntervalTicks(TickRate.Default));

  /// <summary>El tick siguiente: con N ≥ 2 no lleva posiciones.</summary>
  private static readonly TickId QuietTick = PositionTick.Next();

  private static readonly EntityId Unit = new(3);

  private readonly RecordingOutbox _outbox = new();
  private readonly StateSyncPublisher _publisher;

  public StateSyncPublisherTests() =>
      _publisher = new StateSyncPublisher(new StateProjector(Config, TickRate.Default), _outbox, CatalogFixture.Create());

  [Fact]
  public void Publish_EnqueuesP1ThenP2InProjectedOrder()
  {
    DomainEvent orderChanged = new(new EventStamp(PositionTick, 0), new UnitOrderChanged(Unit, new OrderId(1), UnitOrderKind.Move));

    _publisher.Publish(new TickPublication(PositionTick, MapRevision.Initial, [orderChanged], default,
        [new UnitPosition(Unit, default)], null));

    Assert.Equal(
        [(PlayerId.One, ProtocolMessageTypes.StateEvents, DeliveryClass.Essential),
         (PlayerId.One, ProtocolMessageTypes.PositionUpdate, DeliveryClass.Supersedable),
         (PlayerId.Two, ProtocolMessageTypes.StateEvents, DeliveryClass.Essential),
         (PlayerId.Two, ProtocolMessageTypes.PositionUpdate, DeliveryClass.Supersedable)],
        _outbox.Shape());
  }

  [Fact]
  public void Publish_NothingToSend_EnqueuesNothing()
  {
    _publisher.Publish(new TickPublication(QuietTick, MapRevision.Initial, default, default, default, null));

    Assert.Empty(_outbox.Enqueued);
  }

  [Fact]
  public void PublishInitialState_EnqueuesOneEssentialInitialStatePerPlayer()
  {
    SessionId session = SessionId.New();

    _publisher.PublishInitialState(session, InitializationId.First, World());

    Assert.Equal(
        [(PlayerId.One, ProtocolMessageTypes.InitialState, DeliveryClass.Essential),
         (PlayerId.Two, ProtocolMessageTypes.InitialState, DeliveryClass.Essential)],
        _outbox.Shape());

    foreach ((PlayerId recipient, OutboundMessage message) in _outbox.Enqueued)
    {
      InitialStateDto dto = Assert.IsType<InitialStateDto>(message.Payload);
      Assert.Equal(recipient.Value, dto.PlayerId);
      Assert.Equal(session.ToString(), dto.SessionId);
      Assert.Equal(InitializationId.First.Value, dto.InitializationId);
    }
  }

  /// <summary>Mundo mínimo SOLO para pruebas: los dos Centros Principales y los saldos.</summary>
  private static WorldView World() => new(
      TickId.Zero,
      new MapView(new GridSpec(1.0, 40, 30), MapRevision.Initial),
      [new PlayerView(PlayerId.One, new ResourceAmount(100, 50)), new PlayerView(PlayerId.Two, new ResourceAmount(100, 50))],
      default,
      [new BuildingView(new EntityId(10), PlayerId.One, BuildingType.MainCenter, new Footprint(new GridCell(2, 2), new FootprintSize(4, 4)), 1000, true, null),
       new BuildingView(new EntityId(20), PlayerId.Two, BuildingType.MainCenter, new Footprint(new GridCell(34, 24), new FootprintSize(4, 4)), 1000, true, null)],
      default,
      default);

  /// <summary>Outbox falso: registra cada <c>Enqueue</c> en orden, sin colas ni red.</summary>
  private sealed class RecordingOutbox : IClientOutbox
  {
    public List<(PlayerId Recipient, OutboundMessage Message)> Enqueued { get; } = [];

    public void Enqueue(PlayerId recipient, OutboundMessage message) => Enqueued.Add((recipient, message));

    /// <summary>Destinatario, tipo y clase de entrega de cada mensaje, en orden.</summary>
    public IEnumerable<(PlayerId, string, DeliveryClass)> Shape() =>
        Enqueued.Select(e => (e.Recipient, e.Message.Type, e.Message.Delivery));
  }
}
