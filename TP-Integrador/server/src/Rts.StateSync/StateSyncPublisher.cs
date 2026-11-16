using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Shared.Ids;
using Rts.Shared.World;
using Rts.StateSync.Projection;

namespace Rts.StateSync;

/// <summary>
/// Implementación de <see cref="IStateSyncPublisher"/> (D-064): proyecta y encola en la misma llamada, en orden P1 → P2, sin hilos,
/// colas propias ni I/O.
/// </summary>
public sealed class StateSyncPublisher : IStateSyncPublisher
{
  private readonly IStateProjector _projector;
  private readonly IClientOutbox _outbox;
  private readonly GameCatalog _catalog;

  public StateSyncPublisher(IStateProjector projector, IClientOutbox outbox, GameCatalog catalog)
  {
    ArgumentNullException.ThrowIfNull(projector);
    ArgumentNullException.ThrowIfNull(outbox);
    ArgumentNullException.ThrowIfNull(catalog);

    _projector = projector;
    _outbox = outbox;
    _catalog = catalog;
  }

  public void Publish(TickPublication publication)
  {
    TickProjection projection = _projector.ProjectTick(publication);
    Enqueue(projection.Player1);
    Enqueue(projection.Player2);
  }

  public void PublishInitialState(SessionId session, InitializationId initialization, WorldView world)
  {
    foreach (PlayerId player in new[] { PlayerId.One, PlayerId.Two })
    {
      var request = new InitialStateRequest(session, initialization, player, world, _catalog);
      _outbox.Enqueue(player, new OutboundMessage(ProtocolMessageTypes.InitialState, _projector.BuildInitialState(request), DeliveryClass.Essential));
    }
  }

  private void Enqueue(RecipientMessages recipient)
  {
    foreach (OutboundMessage message in recipient.Messages)
    {
      _outbox.Enqueue(recipient.Recipient, message);
    }
  }
}
