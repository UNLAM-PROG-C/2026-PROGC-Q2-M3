using System.Text.Json.Serialization;
using Rts.Protocol.Outbound;
using Rts.Shared.Commands;
using Rts.Shared.Entities;

namespace Rts.Protocol.State;

/// <summary>
/// Lote de un tick para un destinatario (09 §2, §4): sus eventos visibles en el orden oficial y los resultados de <b>sus</b>
/// comandos procesados en ese tick (RECOMENDADO D-070). No se envía si ambas listas quedan vacías.
/// </summary>
public sealed record StateEventsDto(
    long Tick,
    long MapRevision,
    IReadOnlyList<StateEventDto> Events,
    IReadOnlyList<CommandResultDto> CommandResults) : IOutboundPayload;

/// <summary>Resultado oficial de un comando, solo para su emisor. <see cref="Error"/> es un <c>ErrorCode</c> como string.</summary>
public sealed record CommandResultDto(
    long ClientCommandId,
    long Sequence,
    long Tick,
    CommandOutcome Outcome,
    string? Error,
    IReadOnlyList<EntityResultDto> EntityResults);

public sealed record EntityResultDto(int EntityId, string? Error);

/// <summary>
/// Evento publicado. Mapeo 1:1 con los eventos de dominio (RECOMENDADO D-070): el discriminador <c>type</c> es el nombre del
/// evento de dominio. <see cref="Index"/> es su <c>IndexInTick</c> oficial; puede tener huecos porque cada destinatario solo
/// recibe lo que puede ver. Visibilidad en el contrato 09 §3.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BuildingPlacedDto), "BuildingPlaced")]
[JsonDerivedType(typeof(BuildingProgressChangedDto), "BuildingProgressChanged")]
[JsonDerivedType(typeof(BuildingCompletedDto), "BuildingCompleted")]
[JsonDerivedType(typeof(FarmDepletedDto), "FarmDepleted")]
[JsonDerivedType(typeof(FarmRepairStartedDto), "FarmRepairStarted")]
[JsonDerivedType(typeof(FarmRepairProgressChangedDto), "FarmRepairProgressChanged")]
[JsonDerivedType(typeof(FarmRepairCompletedDto), "FarmRepairCompleted")]
[JsonDerivedType(typeof(MapRevisionChangedDto), "MapRevisionChanged")]
[JsonDerivedType(typeof(EntityDamagedDto), "EntityDamaged")]
[JsonDerivedType(typeof(EntityDestroyedDto), "EntityDestroyed")]
[JsonDerivedType(typeof(TreeDepletedDto), "TreeDepleted")]
[JsonDerivedType(typeof(TreeRespawnedDto), "TreeRespawned")]
[JsonDerivedType(typeof(UnitOrderChangedDto), "UnitOrderChanged")]
[JsonDerivedType(typeof(RepathRequestedDto), "RepathRequested")]
[JsonDerivedType(typeof(UnitSpawnedDto), "UnitSpawned")]
[JsonDerivedType(typeof(ProductionChangedDto), "ProductionChanged")]
[JsonDerivedType(typeof(ResourcesChangedDto), "ResourcesChanged")]
public abstract record StateEventDto(int Index);

// Ambos jugadores.
public sealed record BuildingPlacedDto(int Index, int BuildingId, int Owner, BuildingType BuildingType, FootprintDto Footprint, int Hp) : StateEventDto(Index);

public sealed record BuildingProgressChangedDto(int Index, int BuildingId, int Hp) : StateEventDto(Index);

public sealed record BuildingCompletedDto(int Index, int BuildingId) : StateEventDto(Index);

public sealed record FarmDepletedDto(int Index, int FarmId) : StateEventDto(Index);

public sealed record FarmRepairStartedDto(int Index, int FarmId) : StateEventDto(Index);

public sealed record FarmRepairProgressChangedDto(int Index, int FarmId, int WorkDone, int WorkRequired) : StateEventDto(Index);

public sealed record FarmRepairCompletedDto(int Index, int FarmId) : StateEventDto(Index);

public sealed record MapRevisionChangedDto(int Index, long Revision) : StateEventDto(Index);

public sealed record EntityDamagedDto(int Index, int EntityId, int Hp) : StateEventDto(Index);

public sealed record EntityDestroyedDto(int Index, int EntityId) : StateEventDto(Index);

public sealed record TreeDepletedDto(int Index, int TreeId) : StateEventDto(Index);

public sealed record TreeRespawnedDto(int Index, int TreeId) : StateEventDto(Index);

/// <summary>Visible para ambos (DECIDIDO D-067).</summary>
public sealed record UnitOrderChangedDto(int Index, int UnitId, long OrderId, UnitOrderKind Kind) : StateEventDto(Index);

public sealed record UnitSpawnedDto(int Index, int UnitId, int Owner, UnitType UnitType, double X, double Z, int Hp) : StateEventDto(Index);

// Solo el dueño.

/// <summary>Exactamente uno de <see cref="Destination"/> o <see cref="TargetId"/> está presente.</summary>
public sealed record RepathRequestedDto(int Index, int UnitId, long OrderId, long MapRevision, PositionDto? Destination, int? TargetId, bool IsPaused) : StateEventDto(Index);

public sealed record ProductionChangedDto(int Index, int ProducerId, ProducerStatus Status, UnitType? ActiveUnitType, int? RemainingTicks, IReadOnlyList<UnitType> Pending) : StateEventDto(Index);

/// <summary>Saldo propio después del tick. No lleva PlayerId: solo lo recibe el dueño.</summary>
public sealed record ResourcesChangedDto(int Index, int Wood, int Food) : StateEventDto(Index);
