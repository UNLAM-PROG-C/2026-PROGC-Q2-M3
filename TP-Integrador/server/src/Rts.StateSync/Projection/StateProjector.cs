using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.State;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;
using Rts.StateSync.Visibility;

namespace Rts.StateSync.Projection;

/// <summary>Implementación de <see cref="IStateProjector"/>: convierte vistas confirmadas en DTOs por destinatario, sin estado ni I/O (contrato 09 §1).</summary>
public sealed class StateProjector : IStateProjector
{
  private readonly IEventAudiencePolicy _audience = new EventAudiencePolicy();

  public StateProjector(StateSyncConfig config, TickRate tickRate)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate(tickRate);
  }

  public InitialStateDto BuildInitialState(InitialStateRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    WorldView world = request.World;
    PlayerView recipient = world.Players.Single(p => p.Player == request.Recipient);

    return new InitialStateDto(
        request.Session.ToString(),
        request.Initialization.Value,
        request.Recipient.Value,
        world.Tick.Value,
        new MapDto(world.Map.Grid.CellSize, world.Map.Grid.Columns, world.Map.Grid.Rows, world.Map.Revision.Value),
        [.. world.Players.Select(p => new PlayerPublicDto(p.Player.Value, MainCenterOf(world, p.Player).Value))],
        [.. world.Units.Select(ToDto)],
        [.. world.Buildings.Select(ToDto)],
        [.. world.Trees.Select(ToDto)],
        ToDto(recipient.Resources),
        [.. world.Producers.Where(p => p.Owner == request.Recipient).Select(ToDto)],
        ToDto(request.Catalog));
  }

  public TickProjection ProjectTick(TickPublication publication)
  {
    ArgumentNullException.ThrowIfNull(publication);
    return new TickProjection(publication.Tick, ForRecipient(publication, PlayerId.One), ForRecipient(publication, PlayerId.Two));
  }

  private RecipientMessages ForRecipient(TickPublication publication, PlayerId recipient)
  {
    StateEventDto[] events = [.. publication.Events.Where(e => _audience.GetAudience(e.Payload).Includes(recipient)).Select(ToDto)];
    CommandResultDto[] results = [.. publication.CommandResults.Where(r => r.Key.Player == recipient).Select(ToDto)];

    if (events.Length == 0 && results.Length == 0)
    {
      return new RecipientMessages(recipient, []);
    }

    var batch = new StateEventsDto(publication.Tick.Value, publication.MapRevision.Value, events, results);
    return new RecipientMessages(recipient, [new OutboundMessage(ProtocolMessageTypes.StateEvents, batch, DeliveryClass.Essential)]);
  }

  private static EntityId MainCenterOf(WorldView world, PlayerId player) =>
      world.Buildings.Single(b => b.Owner == player && b.BuildingType == BuildingType.MainCenter).BuildingId;

  private static UnitDto ToDto(UnitView u) =>
      new(u.UnitId.Value, u.Owner.Value, u.UnitType, u.Position.X, u.Position.Z, u.Hp, u.Order);

  private static BuildingDto ToDto(BuildingView b) =>
      new(b.BuildingId.Value, b.Owner.Value, b.BuildingType,
          new FootprintDto(b.Footprint.Anchor.Column, b.Footprint.Anchor.Row, b.Footprint.Size.Width, b.Footprint.Size.Height),
          b.Hp, b.IsCompleted, b.Farm is { } f ? new FarmDto(f.IsDepleted, f.IsRepairing) : null);

  private static TreeDto ToDto(TreeView t) =>
      new(t.TreeId.Value, t.Cell.Column, t.Cell.Row, t.Position.X, t.Position.Z, t.IsAvailable);

  private static ProducerDto ToDto(ProducerView p) =>
      new(p.ProducerId.Value, p.Status, p.ActiveUnitType, p.RemainingTicks, [.. p.PendingUnitTypes]);

  private static CatalogDto ToDto(GameCatalog c) =>
      new(c.TickRate.TicksPerSecond, [.. c.Units.Select(ToDto)], [.. c.Buildings.Select(ToDto)],
          ToDto(c.FarmRepairCost), c.FarmRepairWorkRequired, c.GatherRange, c.BuildRange);

  private static UnitCatalogDto ToDto(UnitCatalogEntry u) =>
      new(u.UnitType, u.MaxHp, ToDto(u.Cost), u.TrainingDurationTicks, u.SpeedPerSecond, ToDto(u.Attack));

  private static BuildingCatalogDto ToDto(BuildingCatalogEntry b) =>
      new(b.BuildingType, b.MaxHp, b.Footprint.Width, b.Footprint.Height, b.Cost is { } cost ? ToDto(cost) : null, ToDto(b.Attack));

  private static AttackDto? ToDto(AttackStatsView? a) =>
      a is null ? null : new(a.Damage, a.Range, a.CooldownTicks);

  private static ResourcesDto ToDto(ResourceAmount r) => new(r.Wood, r.Food);

  private static StateEventDto ToDto(DomainEvent e) => e.Payload switch
  {
    UnitOrderChanged p => new UnitOrderChangedDto(e.Stamp.IndexInTick, p.UnitId.Value, p.OrderId.Value, p.Kind),
    _ => throw new NotSupportedException($"Evento sin DTO: {e.Payload.GetType().Name}."),
  };

  private static CommandResultDto ToDto(CommandResult r) =>
      new(r.Key.ClientCommandId.Value, r.Sequence.Value, r.Tick.Value, r.Outcome, r.Error?.Value,
          [.. r.EntityResults.Select(x => new EntityResultDto(x.Entity.Value, x.Error?.Value))]);
}
