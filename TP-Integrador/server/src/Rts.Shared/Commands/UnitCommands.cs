using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Shared.Commands;

// Payloads de comandos de órdenes de unidad (D-009). Las unidades del comando son las de Routes/UnitIds;
// cada una se acepta o rechaza individualmente (04 §2). RouteMapRevision es la revisión que usó Godot:
// el servidor valida siempre contra el mapa actual (04 §4). Nunca incluyen PlayerId (viene del CommandEnvelope).

/// <summary>Mover unidades a un mismo destino, sin formaciones (04 §2). Al llegar, Idle.</summary>
public sealed record MoveCommand : IGameCommand
{
  public MoveCommand(PositionXZ destination, MapRevision routeMapRevision, EquatableArray<UnitRouteCandidate> routes)
  {
    CommandGuards.EnsureFinite(destination, nameof(destination));
    CommandGuards.EnsureRoutes(routes, nameof(routes));
    Destination = destination;
    RouteMapRevision = routeMapRevision;
    Routes = routes;
  }

  public CommandType Type => CommandType.Move;

  public PositionXZ Destination { get; }

  public MapRevision RouteMapRevision { get; }

  public EquatableArray<UnitRouteCandidate> Routes { get; }
}

/// <summary>Cancelar la orden activa de las unidades, sin revertir progreso ni recursos (04 §2).</summary>
public sealed record StopCommand : IGameCommand
{
  public StopCommand(EquatableArray<EntityId> unitIds)
  {
    if (unitIds.IsEmpty)
    {
      throw new ArgumentException("El comando debe incluir al menos una unidad.", nameof(unitIds));
    }

    if (unitIds.Any(id => id.IsNone) || unitIds.Distinct().Count() != unitIds.Count)
    {
      throw new ArgumentException("Las unidades deben ser válidas y no repetirse.", nameof(unitIds));
    }

    UnitIds = unitIds;
  }

  public CommandType Type => CommandType.Stop;

  public EquatableArray<EntityId> UnitIds { get; }
}

/// <summary>Atacar una unidad o edificio enemigo (02 §2). Solo soldados.</summary>
public sealed record AttackCommand : IGameCommand
{
  public AttackCommand(EntityId targetId, MapRevision routeMapRevision, EquatableArray<UnitRouteCandidate> routes)
  {
    CommandGuards.EnsureEntity(targetId, nameof(targetId));
    CommandGuards.EnsureRoutes(routes, nameof(routes));
    TargetId = targetId;
    RouteMapRevision = routeMapRevision;
    Routes = routes;
  }

  public CommandType Type => CommandType.Attack;

  public EntityId TargetId { get; }

  public MapRevision RouteMapRevision { get; }

  public EquatableArray<UnitRouteCandidate> Routes { get; }
}

/// <summary>Recolectar madera de un árbol (01 §3). Solo trabajadores.</summary>
public sealed record GatherWoodCommand : IGameCommand
{
  public GatherWoodCommand(EntityId treeId, MapRevision routeMapRevision, EquatableArray<UnitRouteCandidate> routes)
  {
    CommandGuards.EnsureEntity(treeId, nameof(treeId));
    CommandGuards.EnsureRoutes(routes, nameof(routes));
    TreeId = treeId;
    RouteMapRevision = routeMapRevision;
    Routes = routes;
  }

  public CommandType Type => CommandType.GatherWood;

  public EntityId TreeId { get; }

  public MapRevision RouteMapRevision { get; }

  public EquatableArray<UnitRouteCandidate> Routes { get; }
}

/// <summary>
/// Orden Build para trabajadores (03 §2-3), con dos formas (D-032):
/// <list type="bullet">
/// <item><see cref="Place"/>: colocar un edificio nuevo (tipo + celda ancla); paga madera en el mismo commit atómico.</item>
/// <item><see cref="Continue"/>: sumar trabajadores a una obra propia que ya existe; sin costo.</item>
/// </list>
/// Las vallas se colocan con un <see cref="Place"/> por celda (D-033). Las rutas siguen las reglas de Movement.
/// </summary>
public sealed record BuildCommand : IGameCommand
{
  private BuildCommand(
      BuildingType? buildingType,
      GridCell? anchor,
      EntityId? existingBuildingId,
      MapRevision routeMapRevision,
      EquatableArray<UnitRouteCandidate> routes)
  {
    CommandGuards.EnsureRoutes(routes, nameof(routes));
    BuildingType = buildingType;
    Anchor = anchor;
    ExistingBuildingId = existingBuildingId;
    RouteMapRevision = routeMapRevision;
    Routes = routes;
  }

  public CommandType Type => CommandType.Build;

  /// <summary>Tipo a colocar; nulo en la forma <see cref="Continue"/>.</summary>
  public BuildingType? BuildingType { get; }

  /// <summary>Celda ancla (columna y fila mínimas de la huella); nulo en la forma <see cref="Continue"/>.</summary>
  public GridCell? Anchor { get; }

  /// <summary>Obra a continuar; nulo en la forma <see cref="Place"/>.</summary>
  public EntityId? ExistingBuildingId { get; }

  public bool IsPlacement => ExistingBuildingId is null;

  public MapRevision RouteMapRevision { get; }

  public EquatableArray<UnitRouteCandidate> Routes { get; }

  public static BuildCommand Place(
      BuildingType buildingType,
      GridCell anchor,
      MapRevision routeMapRevision,
      EquatableArray<UnitRouteCandidate> routes)
  {
    if (!Enum.IsDefined(buildingType))
    {
      throw new ArgumentException($"Tipo de edificio inválido: {buildingType}.", nameof(buildingType));
    }

    return new BuildCommand(buildingType, anchor, null, routeMapRevision, routes);
  }

  public static BuildCommand Continue(
      EntityId buildingId,
      MapRevision routeMapRevision,
      EquatableArray<UnitRouteCandidate> routes)
  {
    CommandGuards.EnsureEntity(buildingId, nameof(buildingId));
    return new BuildCommand(null, null, buildingId, routeMapRevision, routes);
  }
}

/// <summary>Reparar la capacidad productiva de una granja agotada (03 §4). Solo trabajadores.</summary>
public sealed record RepairFarmCommand : IGameCommand
{
  public RepairFarmCommand(EntityId farmId, MapRevision routeMapRevision, EquatableArray<UnitRouteCandidate> routes)
  {
    CommandGuards.EnsureEntity(farmId, nameof(farmId));
    CommandGuards.EnsureRoutes(routes, nameof(routes));
    FarmId = farmId;
    RouteMapRevision = routeMapRevision;
    Routes = routes;
  }

  public CommandType Type => CommandType.RepairFarm;

  public EntityId FarmId { get; }

  public MapRevision RouteMapRevision { get; }

  public EquatableArray<UnitRouteCandidate> Routes { get; }
}

/// <summary>
/// Ruta nueva que Godot entrega tras un <c>RepathRequested</c> (04 §5). Entra por la cola global con Sequence (D-028).
/// Se rechaza sin tocar la orden vigente si <see cref="OrderId"/> no coincide con la orden actual de la unidad.
/// </summary>
public sealed record RepathResponseCommand : IGameCommand
{
  public RepathResponseCommand(
      EntityId unitId,
      OrderId orderId,
      MapRevision routeMapRevision,
      EquatableArray<PositionXZ> waypoints)
  {
    CommandGuards.EnsureEntity(unitId, nameof(unitId));
    if (orderId.IsNone)
    {
      throw new ArgumentException("La respuesta debe indicar la orden a la que corresponde.", nameof(orderId));
    }

    CommandGuards.EnsureFinite(waypoints, nameof(waypoints));
    UnitId = unitId;
    OrderId = orderId;
    RouteMapRevision = routeMapRevision;
    Waypoints = waypoints;
  }

  public CommandType Type => CommandType.RepathResponse;

  public EntityId UnitId { get; }

  public OrderId OrderId { get; }

  public MapRevision RouteMapRevision { get; }

  public EquatableArray<PositionXZ> Waypoints { get; }
}
