using Rts.Economy.Model;
using Rts.Production.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.World;

namespace Rts.Simulation.State;

/// <summary>
/// Traduce el <see cref="WorldState"/> interno a <see cref="WorldView"/>, el modelo de lectura que Simulation entrega hacia afuera
/// (D-049): GameSession lo pasa a State Sync para armar el InitialState. Solo copia datos confirmados; no decide visibilidad
/// (los saldos y los productores son privados y los filtra State Sync). Todas las colecciones salen ordenadas por ID, porque
/// los diccionarios del <see cref="WorldState"/> ya lo están.
/// </summary>
public static class WorldViewProjector
{
  public static WorldView Project(WorldState world)
  {
    ArgumentNullException.ThrowIfNull(world);
    return new WorldView(
        world.Tick,
        new MapView(world.Grid, world.MapRevision),
        [.. world.Players.Values.Select(p => new PlayerView(p.Player, p.Resources))],
        [.. world.Units.Values.Select(ToView)],
        [.. world.Buildings.Values.Select(ToView)],
        [.. world.Trees.Values.Select(ToView)],
        [.. world.Buildings.Values.Where(b => b.Production is not null).Select(b => ToView(b.Production!))]);
  }

  private static UnitView ToView(UnitEntity unit) =>
      new(unit.Id, unit.Owner, unit.Type, unit.Position, unit.Hp, unit.Order.Kind);

  private static BuildingView ToView(BuildingEntity building) => new(
      building.Id, building.Owner, building.Type, building.Footprint, building.Hp, building.IsCompleted,
      building.FarmEconomy is { } farm ? new FarmPublicState(farm.Status == FarmProductionStatus.Depleted, building.Repair is not null) : null);

  private static TreeView ToView(TreeEntity tree) =>
      new(tree.Id, tree.Cell, tree.Position, tree.Economy.Status == TreeStatus.Available);

  private static ProducerView ToView(ProducerState producer) => new(
      producer.ProducerId,
      producer.Owner,
      producer.Status,
      producer.ActiveJob?.Request.UnitType,
      producer.ActiveJob?.RemainingTicks,
      [.. producer.Pending.Select(request => request.UnitType)]);
}
