using Rts.Protocol.Outbound;
using Rts.Shared.Entities;

namespace Rts.Protocol.State;

// DTOs del InitialState (09 §3). Uno por destinatario: los datos privados (recursos y producción) son solo del destinatario.

/// <summary>Mundo inicial para un destinatario. Godot responde <c>ClientReady</c> con <see cref="InitializationId"/>.</summary>
public sealed record InitialStateDto(
    string SessionId,
    long InitializationId,
    int PlayerId,
    long Tick,
    MapDto Map,
    IReadOnlyList<PlayerPublicDto> Players,
    IReadOnlyList<UnitDto> Units,
    IReadOnlyList<BuildingDto> Buildings,
    IReadOnlyList<TreeDto> Trees,
    ResourcesDto OwnResources,
    IReadOnlyList<ProducerDto> OwnProduction,
    CatalogDto Catalog) : IOutboundPayload;

/// <summary>Grilla del mapa: X ∈ [0, columns·cellSize), Z ∈ [0, rows·cellSize) (D-004, D-029).</summary>
public sealed record MapDto(double CellSize, int Columns, int Rows, long Revision);

/// <summary>Datos públicos de un jugador: su ID y su Centro Principal. Nunca incluye recursos ni colas.</summary>
public sealed record PlayerPublicDto(int PlayerId, int MainCenterId);

/// <summary>Rectángulo de celdas [column, column + width) × [row, row + height).</summary>
public sealed record FootprintDto(int Column, int Row, int Width, int Height);

/// <summary>Posición oficial X/Z en metros.</summary>
public sealed record PositionDto(double X, double Z);

public sealed record UnitDto(int UnitId, int Owner, UnitType UnitType, double X, double Z, int Hp, UnitOrderKind Order);

/// <summary><see cref="Farm"/> solo para granjas completadas.</summary>
public sealed record BuildingDto(int BuildingId, int Owner, BuildingType BuildingType, FootprintDto Footprint, int Hp, bool IsCompleted, FarmDto? Farm);

public sealed record FarmDto(bool IsDepleted, bool IsRepairing);

/// <summary>Árbol: celda reservada y posición (centro de la celda). Conserva su ID aunque esté agotado.</summary>
public sealed record TreeDto(int TreeId, int Column, int Row, double X, double Z, bool IsAvailable);

public sealed record ResourcesDto(int Wood, int Food);

/// <summary>Productor propio: tarea activa y cola pendiente en orden FIFO.</summary>
public sealed record ProducerDto(int ProducerId, ProducerStatus Status, UnitType? ActiveUnitType, int? RemainingTicks, IReadOnlyList<UnitType> Pending);

/// <summary>Catálogo estático, igual para ambos (DECIDIDO D-068).</summary>
public sealed record CatalogDto(
    int TicksPerSecond,
    IReadOnlyList<UnitCatalogDto> Units,
    IReadOnlyList<BuildingCatalogDto> Buildings,
    ResourcesDto FarmRepairCost,
    int FarmRepairWorkRequired,
    double GatherRange,
    double BuildRange);

public sealed record UnitCatalogDto(UnitType UnitType, int MaxHp, ResourcesDto Cost, int TrainingDurationTicks, double SpeedPerSecond, AttackDto? Attack);

/// <summary><see cref="Cost"/> se omite para el Centro Principal.</summary>
public sealed record BuildingCatalogDto(BuildingType BuildingType, int MaxHp, int FootprintWidth, int FootprintHeight, ResourcesDto? Cost, AttackDto? Attack);

public sealed record AttackDto(int Damage, double Range, int CooldownTicks);
