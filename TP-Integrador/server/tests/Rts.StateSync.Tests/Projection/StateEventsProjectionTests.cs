using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.State;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;
using Rts.StateSync.Projection;

namespace Rts.StateSync.Tests.Projection;

public class StateEventsProjectionTests
{
  /// <summary>Tick impar: con N = 2 no lleva posiciones, así que estas pruebas siguen valiendo cuando llegue 1.4.</summary>
  private static readonly TickId Tick = new(7);
  private static readonly MapRevision Revision = new(4);
  private static readonly ClientCommandId P1CommandId = new(5);
  private static readonly CommandSequence P1Sequence = new(9);
  private static readonly ClientCommandId P2CommandId = new(2);
  private static readonly CommandSequence P2Sequence = new(10);

  private readonly StateProjector _projector = new(new StateSyncConfig(), TickRate.Default);

  [Fact]
  public void ProjectTick_PublicEventsGoToBothInOfficialOrder()
  {
    TickProjection projection = _projector.ProjectTick(Publication(
        events: [OrderChanged(index: 2, unitId: 3, orderId: 10, UnitOrderKind.Move), OrderChanged(index: 5, unitId: 4, orderId: 11, UnitOrderKind.Idle)]));

    foreach (RecipientMessages recipient in new[] { projection.Player1, projection.Player2 })
    {
      StateEventsDto batch = SingleBatch(recipient);
      Assert.Equal(Tick.Value, batch.Tick);
      Assert.Equal(Revision.Value, batch.MapRevision);
      Assert.Equal(
          [new UnitOrderChangedDto(2, 3, 10, UnitOrderKind.Move), new UnitOrderChangedDto(5, 4, 11, UnitOrderKind.Idle)],
          batch.Events);
      Assert.Empty(batch.CommandResults);
    }
  }

  [Fact]
  public void ProjectTick_CommandResultsOnlyToSender()
  {
    var p1Partial = new CommandResult(new IdempotencyKey(PlayerId.One, P1CommandId), P1Sequence, Tick,
        CommandOutcome.PartiallyAccepted, null,
        [EntityCommandResult.Accepted(new EntityId(3)), new EntityCommandResult(new EntityId(8), CommonErrors.NotOwner)]);
    var p2Rejected = new CommandResult(new IdempotencyKey(PlayerId.Two, P2CommandId), P2Sequence, Tick,
        CommandOutcome.Rejected, CommonErrors.EntityNotFound, []);

    TickProjection projection = _projector.ProjectTick(Publication(results: [p1Partial, p2Rejected]));

    StateEventsDto p1 = SingleBatch(projection.Player1);
    Assert.Empty(p1.Events);
    CommandResultDto p1Result = Assert.Single(p1.CommandResults);
    Assert.Equal((P1CommandId.Value, P1Sequence.Value, Tick.Value, CommandOutcome.PartiallyAccepted, null),
        (p1Result.ClientCommandId, p1Result.Sequence, p1Result.Tick, p1Result.Outcome, p1Result.Error));
    Assert.Equal([new EntityResultDto(3, null), new EntityResultDto(8, CommonErrors.NotOwner.Value)], p1Result.EntityResults);

    CommandResultDto p2Result = Assert.Single(SingleBatch(projection.Player2).CommandResults);
    Assert.Equal(P2CommandId.Value, p2Result.ClientCommandId);
    Assert.Equal(CommandOutcome.Rejected, p2Result.Outcome);
    Assert.Equal(CommonErrors.EntityNotFound.Value, p2Result.Error);
    Assert.Empty(p2Result.EntityResults);
  }

  [Fact]
  public void ProjectTick_NoBatchWhenNothingForRecipient()
  {
    TickProjection projection = _projector.ProjectTick(Publication());

    Assert.Equal(Tick, projection.Tick);
    Assert.Equal(PlayerId.One, projection.Player1.Recipient);
    Assert.Equal(PlayerId.Two, projection.Player2.Recipient);
    Assert.Empty(projection.Player1.Messages);
    Assert.Empty(projection.Player2.Messages);
  }

  [Fact]
  public void ProjectTick_ConstructionAndMapEventsGoToBoth()
  {
    var building = new EntityId(20);
    var footprint = new Footprint(new GridCell(4, 6), new FootprintSize(2, 3));
    const int PlacedHp = 1;
    const int ProgressHp = 40;
    MapRevision newRevision = Revision.Next();

    DomainEvent placed = Event(0, new BuildingPlaced(building, PlayerId.Two, BuildingType.Farm, footprint, PlacedHp));
    DomainEvent revisionChanged = Event(1, new MapRevisionChanged(newRevision));
    DomainEvent progress = Event(2, new BuildingProgressChanged(building, ProgressHp));
    DomainEvent completed = Event(3, new BuildingCompleted(building));

    TickProjection projection = _projector.ProjectTick(Publication(events: [placed, revisionChanged, progress, completed]));

    var footprintDto = new FootprintDto(footprint.Anchor.Column, footprint.Anchor.Row, footprint.Size.Width, footprint.Size.Height);
    foreach (RecipientMessages recipient in new[] { projection.Player1, projection.Player2 })
    {
      Assert.Equal(
          [new BuildingPlacedDto(placed.Stamp.IndexInTick, building.Value, PlayerId.Two.Value, BuildingType.Farm, footprintDto, PlacedHp),
           new MapRevisionChangedDto(revisionChanged.Stamp.IndexInTick, newRevision.Value),
           new BuildingProgressChangedDto(progress.Stamp.IndexInTick, building.Value, ProgressHp),
           new BuildingCompletedDto(completed.Stamp.IndexInTick, building.Value)],
          SingleBatch(recipient).Events);
    }
  }

  private static DomainEvent Event(int index, IDomainEventPayload payload) => new(new EventStamp(Tick, index), payload);

  private static DomainEvent OrderChanged(int index, int unitId, long orderId, UnitOrderKind kind) =>
      new(new EventStamp(Tick, index), new UnitOrderChanged(new EntityId(unitId), new OrderId(orderId), kind));

  private static TickPublication Publication(EquatableArray<DomainEvent> events = default, EquatableArray<CommandResult> results = default) =>
      new(Tick, Revision, events, results, default, null);

  /// <summary>Exige un único mensaje, que sea un StateEvents esencial, y devuelve su lote.</summary>
  private static StateEventsDto SingleBatch(RecipientMessages recipient)
  {
    OutboundMessage message = Assert.Single(recipient.Messages);
    Assert.Equal(ProtocolMessageTypes.StateEvents, message.Type);
    Assert.Equal(DeliveryClass.Essential, message.Delivery);
    return Assert.IsType<StateEventsDto>(message.Payload);
  }
}
