using Rts.Protocol.State;
using Rts.Shared.Entities;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;

namespace Rts.StateSync.Projection;

/// <summary>Implementación de <see cref="IStateProjector"/>: convierte vistas confirmadas en DTOs por destinatario, sin estado ni I/O (contrato 09 §1).</summary>
public sealed class StateProjector : IStateProjector
{
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

  public TickProjection ProjectTick(TickPublication publication) =>
      throw new NotImplementedException("PENDIENTE 1.3 y 1.4 (contrato 09 §5-6).");

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
}
