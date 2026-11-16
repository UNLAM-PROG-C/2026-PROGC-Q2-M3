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
using Rts.Shared.Match;
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

  [Fact]
  public void ProjectTick_FarmEventsGoToBoth()
  {
    var farm = new EntityId(11);
    const int WorkDone = 30;
    const int WorkRequired = 100;

    DomainEvent depleted = Event(0, new FarmDepleted(farm));
    DomainEvent repairStarted = Event(1, new FarmRepairStarted(farm));
    DomainEvent repairProgress = Event(2, new FarmRepairProgressChanged(farm, WorkDone, WorkRequired));
    DomainEvent repairCompleted = Event(3, new FarmRepairCompleted(farm));

    TickProjection projection = _projector.ProjectTick(Publication(events: [depleted, repairStarted, repairProgress, repairCompleted]));

    foreach (RecipientMessages recipient in new[] { projection.Player1, projection.Player2 })
    {
      Assert.Equal(
          [new FarmDepletedDto(depleted.Stamp.IndexInTick, farm.Value),
           new FarmRepairStartedDto(repairStarted.Stamp.IndexInTick, farm.Value),
           new FarmRepairProgressChangedDto(repairProgress.Stamp.IndexInTick, farm.Value, WorkDone, WorkRequired),
           new FarmRepairCompletedDto(repairCompleted.Stamp.IndexInTick, farm.Value)],
          SingleBatch(recipient).Events);
    }
  }

  [Fact]
  public void ProjectTick_CombatEventsGoToBoth()
  {
    var unit = new EntityId(3);
    const int DamagedHp = 25;

    DomainEvent damaged = Event(0, new EntityDamaged(unit, DamagedHp));
    DomainEvent destroyed = Event(1, new EntityDestroyed(unit));

    TickProjection projection = _projector.ProjectTick(Publication(events: [damaged, destroyed]));

    foreach (RecipientMessages recipient in new[] { projection.Player1, projection.Player2 })
    {
      Assert.Equal(
          [new EntityDamagedDto(damaged.Stamp.IndexInTick, unit.Value, DamagedHp),
           new EntityDestroyedDto(destroyed.Stamp.IndexInTick, unit.Value)],
          SingleBatch(recipient).Events);
    }
  }

  [Fact]
  public void ProjectTick_ProgressAndDamageOnSameSiteAreBothPublishedAndLastHpWins()
  {
    // D-065: el progreso es de la fase 3 y el daño de la fase 6, así que el daño va después y su HP es el final.
    var site = new EntityId(20);
    const int HpAfterProgress = 60;
    const int HpAfterDamage = 52;

    DomainEvent progress = Event(0, new BuildingProgressChanged(site, HpAfterProgress));
    DomainEvent damaged = Event(1, new EntityDamaged(site, HpAfterDamage));

    TickProjection projection = _projector.ProjectTick(Publication(events: [progress, damaged]));

    IReadOnlyList<StateEventDto> events = SingleBatch(projection.Player1).Events;
    Assert.Equal(
        [new BuildingProgressChangedDto(progress.Stamp.IndexInTick, site.Value, HpAfterProgress),
         new EntityDamagedDto(damaged.Stamp.IndexInTick, site.Value, HpAfterDamage)],
        events);
    Assert.Equal(HpAfterDamage, Assert.IsType<EntityDamagedDto>(events.Last()).Hp);
  }

  [Fact]
  public void ProjectTick_GameplayResultIsNotPublished()
  {
    // MatchEnded lo envía GameSession por su propio camino (contrato 09 §2), no State Sync.
    var victory = new MatchResult(MatchResultType.Victory, PlayerId.One, MatchEndReason.BaseDestroyed, Tick);

    TickProjection projection = _projector.ProjectTick(Publication(result: victory));

    Assert.Empty(projection.Player1.Messages);
    Assert.Empty(projection.Player2.Messages);
  }

  private static DomainEvent Event(int index, IDomainEventPayload payload) => new(new EventStamp(Tick, index), payload);

  private static DomainEvent OrderChanged(int index, int unitId, long orderId, UnitOrderKind kind) =>
      new(new EventStamp(Tick, index), new UnitOrderChanged(new EntityId(unitId), new OrderId(orderId), kind));

  private static TickPublication Publication(
      EquatableArray<DomainEvent> events = default, EquatableArray<CommandResult> results = default, MatchResult? result = null) =>
      new(Tick, Revision, events, results, default, result);

  /// <summary>Exige un único mensaje, que sea un StateEvents esencial, y devuelve su lote.</summary>
  private static StateEventsDto SingleBatch(RecipientMessages recipient)
  {
    OutboundMessage message = Assert.Single(recipient.Messages);
    Assert.Equal(ProtocolMessageTypes.StateEvents, message.Type);
    Assert.Equal(DeliveryClass.Essential, message.Delivery);
    return Assert.IsType<StateEventsDto>(message.Payload);
  }
}
