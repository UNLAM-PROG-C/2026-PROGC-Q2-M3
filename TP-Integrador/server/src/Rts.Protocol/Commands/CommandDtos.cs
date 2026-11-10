using Rts.Protocol.State;
using Rts.Shared.Entities;

namespace Rts.Protocol.Commands;

// DTOs de comandos de gameplay, cliente → servidor (04, 05, 03). Nunca llevan PlayerId (08 §3). Networking solo valida su
// estructura y los convierte en el IGameCommand correspondiente; las reglas de gameplay las valida Simulation.

/// <summary>Ruta candidata de una unidad, calculada por Godot en coordenadas X/Z del servidor (D-026, D-029).</summary>
public sealed record UnitRouteDto(int UnitId, IReadOnlyList<PositionDto> Waypoints);

public sealed record MoveCommandDto(PositionDto Destination, long RouteMapRevision, IReadOnlyList<UnitRouteDto> Routes);

public sealed record StopCommandDto(IReadOnlyList<int> UnitIds);

public sealed record AttackCommandDto(int TargetId, long RouteMapRevision, IReadOnlyList<UnitRouteDto> Routes);

public sealed record GatherWoodCommandDto(int TreeId, long RouteMapRevision, IReadOnlyList<UnitRouteDto> Routes);

/// <summary>
/// Dos formas (D-032): colocar (<see cref="BuildingType"/> + <see cref="AnchorColumn"/> + <see cref="AnchorRow"/>) o continuar una
/// obra propia (<see cref="BuildingId"/>). Mezclarlas, o no completar ninguna, es estructura inválida.
/// </summary>
public sealed record BuildCommandDto(
    BuildingType? BuildingType,
    int? AnchorColumn,
    int? AnchorRow,
    int? BuildingId,
    long RouteMapRevision,
    IReadOnlyList<UnitRouteDto> Routes);

public sealed record RepairFarmCommandDto(int FarmId, long RouteMapRevision, IReadOnlyList<UnitRouteDto> Routes);

public sealed record TrainUnitCommandDto(int ProducerId, UnitType UnitType);

/// <summary>Respuesta a un <c>RepathRequested</c> (D-028): misma unidad, misma orden y la revisión de mapa usada.</summary>
public sealed record RepathResponseCommandDto(int UnitId, long OrderId, long MapRevision, IReadOnlyList<PositionDto> Waypoints);
