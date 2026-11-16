using Rts.Protocol;
using Rts.Protocol.Commands;
using Rts.Protocol.State;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Networking.Parsing;

/// <summary>
/// Convierte el DTO de cada comando de gameplay, ya deserializado, en su <see cref="IGameCommand"/>. Lo usa el
/// <see cref="InboundMessageParser"/> después de los controles comunes (sobre, versión, tipo, <c>messageId</c>).
/// <para>
/// Si un campo obligatorio vino en <c>null</c>, o el constructor de dominio rechaza los valores, lanza <see cref="ArgumentException"/>,
/// que el parser traduce en un comando inválido (D-075). Las reglas de gameplay no se validan acá: eso es de Simulation.
/// </para>
/// </summary>
internal static class GameplayCommandReader
{
  /// <summary>El constructor rechaza coordenadas no finitas.</summary>
  public static MoveCommand ToCommand(MoveCommandDto dto)
  {
    if (dto.Destination is null)
    {
      throw new ArgumentException("Move necesita destination.");
    }

    return new MoveCommand(
        new PositionXZ(dto.Destination.X, dto.Destination.Z),
        new MapRevision(dto.RouteMapRevision),
        ReadRoutes(ProtocolMessageTypes.Move, dto.Routes));
  }

  /// <summary>El constructor rechaza la lista vacía y las unidades inválidas o repetidas.</summary>
  public static StopCommand ToCommand(StopCommandDto dto)
  {
    if (dto.UnitIds is null)
    {
      throw new ArgumentException("Stop necesita unitIds.");
    }

    return new StopCommand(dto.UnitIds.Select(id => new EntityId(id)).ToEquatableArray());
  }

  public static AttackCommand ToCommand(AttackCommandDto dto) =>
      new(new EntityId(dto.TargetId), new MapRevision(dto.RouteMapRevision), ReadRoutes(ProtocolMessageTypes.Attack, dto.Routes));

  public static GatherWoodCommand ToCommand(GatherWoodCommandDto dto) =>
      new(new EntityId(dto.TreeId), new MapRevision(dto.RouteMapRevision), ReadRoutes(ProtocolMessageTypes.GatherWood, dto.Routes));

  public static RepairFarmCommand ToCommand(RepairFarmCommandDto dto) =>
      new(new EntityId(dto.FarmId), new MapRevision(dto.RouteMapRevision), ReadRoutes(ProtocolMessageTypes.RepairFarm, dto.Routes));

  /// <summary>Un tipo de unidad desconocido no llega acá: el deserializador ya lo rechaza (enums solo como texto).</summary>
  public static TrainUnitCommand ToCommand(TrainUnitCommandDto dto) => new(new EntityId(dto.ProducerId), dto.UnitType);

  /// <summary>
  /// Una de las dos formas (D-032): colocar (tipo, columna y fila) o continuar una obra (<c>buildingId</c>). Mezclarlas, o no
  /// completar ninguna, es un comando inválido (08 §4).
  /// </summary>
  public static BuildCommand ToCommand(BuildCommandDto dto)
  {
    bool anyPlacementField = dto.BuildingType is not null || dto.AnchorColumn is not null || dto.AnchorRow is not null;
    EquatableArray<UnitRouteCandidate> routes = ReadRoutes(ProtocolMessageTypes.Build, dto.Routes);
    var revision = new MapRevision(dto.RouteMapRevision);

    if (dto.BuildingId is { } buildingId && !anyPlacementField)
    {
      return BuildCommand.Continue(new EntityId(buildingId), revision, routes);
    }

    if (dto.BuildingId is null && dto is { BuildingType: { } buildingType, AnchorColumn: { } column, AnchorRow: { } row })
    {
      return BuildCommand.Place(buildingType, new GridCell(column, row), revision, routes);
    }

    throw new ArgumentException("Build lleva buildingType, anchorColumn y anchorRow para colocar, o solo buildingId para continuar.");
  }

  /// <summary>
  /// Es gameplay como cualquier otro: entra por la cola global con su <c>Sequence</c>, es idempotente por <c>messageId</c> y cuenta
  /// para el límite por segundo (D-028). El constructor rechaza la unidad o la orden inválidas y los waypoints no finitos.
  /// </summary>
  public static RepathResponseCommand ToCommand(RepathResponseCommandDto dto)
  {
    if (dto.Waypoints is null || dto.Waypoints.Any(point => point is null))
    {
      throw new ArgumentException("RepathResponse necesita waypoints, sin elementos nulos.");
    }

    return new RepathResponseCommand(
        new EntityId(dto.UnitId), new OrderId(dto.OrderId), new MapRevision(dto.MapRevision), ToPositions(dto.Waypoints));
  }

  /// <summary>
  /// Las rutas candidatas de un comando de unidades (Move, Attack, GatherWood, Build, RepairFarm). Los nulos que deja el
  /// deserializador se rechazan acá; las rutas vacías y las unidades repetidas o inválidas, en los constructores de dominio.
  /// </summary>
  private static EquatableArray<UnitRouteCandidate> ReadRoutes(string type, IReadOnlyList<UnitRouteDto>? routes)
  {
    if (routes is null || routes.Any(route => route is null || route.Waypoints is null || route.Waypoints.Any(point => point is null)))
    {
      throw new ArgumentException($"{type} necesita routes completas, sin elementos nulos.");
    }

    return routes
        .Select(route => new UnitRouteCandidate(new EntityId(route.UnitId), ToPositions(route.Waypoints)))
        .ToEquatableArray();
  }

  private static EquatableArray<PositionXZ> ToPositions(IEnumerable<PositionDto> points) =>
      points.Select(point => new PositionXZ(point.X, point.Z)).ToEquatableArray();
}
