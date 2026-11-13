using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Economy;
using Rts.Economy.Contracts;
using Rts.Movement;
using Rts.Movement.Model;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// Forma "colocar" de <c>Build</c> (03 §3): crear una obra nueva. Un único commit atómico: si un paso falla, se rechaza el comando entero,
/// sin gasto ni obra. Simulation orquesta, en el orden del contrato:
/// <list type="number">
/// <item>Construction (<see cref="IConstructionRules.EvaluateSite"/>): el sitio contra la ocupación <b>actual</b>. Devuelve la huella.</item>
/// <item>Movement, con <see cref="TaskOrderPlan"/>: cada trabajador contra un <b>mapa hipotético</b> que ya tiene la huella bloqueada.</item>
/// <item>Construction (<see cref="IConstructionRules.EvaluatePlacement"/>): combina sitio y trabajadores. Si ninguno llega, rechaza.</item>
/// <item>Economy (<see cref="IEconomyRules.EvaluateSpend"/>): el costo del edificio. Si no alcanza, rechaza.</item>
/// <item>Commit: madera, obra con ID nuevo, <c>MapRevision</c> + 1 y órdenes. Eventos: <c>BuildingPlaced</c> →
/// <c>MapRevisionChanged</c> → <c>UnitOrderChanged</c> (06 §5).</item>
/// </list>
/// La ocupación se arma desde el mundo en cada comando, así que una colocación anterior del mismo lote ya cuenta: dos obras sobre la
/// misma celda se resuelven por <c>Sequence</c> y la madera se cobra una sola vez (AGENTS §7).
/// </summary>
internal sealed class BuildPlacement
{
  private readonly IConstructionRules _construction;
  private readonly IEconomyRules _economy;
  private readonly IMovementRules _movement;

  public BuildPlacement(IConstructionRules construction, IEconomyRules economy, IMovementRules movement)
  {
    _construction = construction ?? throw new ArgumentNullException(nameof(construction));
    _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
  }

  public CommandResult Handle(CommandEnvelope command, BuildCommand build, BuildingType type, GridCell anchor, TickContext context)
  {
    WorldState world = context.World;
    OccupancyView occupancy = OccupancyOf(world);

    // 1. ¿El sitio sirve?
    SiteEvaluation site = _construction.EvaluateSite(type, anchor, occupancy);
    if (site.Error is { } invalidSite)
    {
      return CommandResult.Rejected(command, context.Tick, invalidSite);
    }

    // 2. ¿Cada trabajador llega, con la obra ya bloqueando el paso? El ID de la obra se reserva acá y solo se usa si todo se acepta.
    Footprint footprint = site.Footprint ?? throw new InvalidOperationException("Construction aceptó el sitio sin devolver la huella.");
    EntityId siteId = world.LastEntityId.Next();
    var order = new TaskOrder(UnitOrderKind.Build, siteId, MovementTarget.WithFootprint(siteId, footprint));
    NavigationGrid hypothetical = context.Navigation.WithBlockedFootprint(footprint);
    TaskOrderPlan plan = TaskOrderPlan.Evaluate(world, command.Player, order, build.Routes, hypothetical, _movement, _ => null);

    // 3. Construction combina sitio y trabajadores.
    PlacementEvaluation placement = _construction.EvaluatePlacement(new PlacementInput(
        command.Player, type, anchor, occupancy, [.. plan.Results.Select(r => new WorkerPlacementCandidate(r.Entity, r.Error))]));
    if (placement.Error is { } rejected)
    {
      return CommandResult.Rejected(command, context.Tick, rejected, placement.WorkerResults);
    }

    EnsureCoherent(placement, footprint, plan);

    // 4. ¿Alcanza la madera?
    SpendEvaluation spend = _economy.EvaluateSpend(world.Players[command.Player].Resources, CostOf(type));
    if (spend.Error is { } insufficient)
    {
      return CommandResult.Rejected(command, context.Tick, insufficient);
    }

    // 5. Commit, todo junto.
    Place(context, command.Player, type, siteId, footprint, placement.InitialHp, spend.BalanceAfter);
    plan.CommitTo(context);
    return GroupCommandSupport.ToResult(command, context.Tick, placement.WorkerResults);
  }

  /// <summary>
  /// Ocupación para validar el sitio (03 §2): huellas de todos los edificios (obras incluidas), celdas de árboles (aunque estén
  /// agotados) y la celda de cada unidad (D-004).
  /// </summary>
  private static OccupancyView OccupancyOf(WorldState world) => new(
      world.Grid,
      world.Buildings.Values.SelectMany(building => building.Footprint.Cells()),
      world.Trees.Values.Select(tree => tree.Cell),
      world.Units.Values.Select(unit => world.Grid.ToCell(unit.Position)));

  /// <summary>Costo del edificio según Economy (D-015). Un tipo sin costo (Centro Principal) no debió pasar el sitio.</summary>
  private ResourceAmount CostOf(BuildingType type) => _economy.TryGetBuildingCost(type, out ResourceAmount cost)
      ? cost
      : throw new InvalidOperationException($"Construction aceptó colocar {type}, que no tiene costo de construcción.");

  /// <summary>
  /// Construction no puede cambiar la huella ni aceptar trabajadores distintos de los que aceptó Movement: si lo hace, la respuesta es
  /// incoherente (falla crítica).
  /// </summary>
  private static void EnsureCoherent(PlacementEvaluation placement, Footprint footprint, TaskOrderPlan plan)
  {
    IEnumerable<EntityId> movementAccepted = plan.Results.Where(r => r.IsAccepted).Select(r => r.Entity);
    if (placement.Footprint != footprint || !placement.AcceptedWorkers.SequenceEqual(movementAccepted))
    {
      throw new InvalidOperationException("Construction aceptó una colocación que no coincide con el sitio o con los trabajadores de Movement.");
    }
  }

  /// <summary>Paga la madera, crea la obra con su ID y su HP inicial, avanza la <c>MapRevision</c> y emite sus dos eventos.</summary>
  private static void Place(
      TickContext context, PlayerId owner, BuildingType type, EntityId siteId, Footprint footprint, int initialHp, ResourceAmount balance)
  {
    WorldState world = context.World;
    var site = new BuildingEntity(
        siteId, owner, type, footprint, initialHp, operationalFromTick: null,
        farmEconomy: null, repair: null, production: null, towerReadyAtTick: null);
    MapRevision revision = world.MapRevision.Next();

    context.World = world.With(
        mapRevision: revision,
        players: world.Players.SetItem(owner, new PlayerState(owner, balance)),
        buildings: world.Buildings.SetItem(siteId, site),
        lastEntityId: siteId);
    context.Emit(new BuildingPlaced(siteId, owner, type, footprint, initialHp));
    context.Emit(new MapRevisionChanged(revision));
  }
}
