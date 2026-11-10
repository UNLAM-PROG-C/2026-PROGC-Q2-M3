using Rts.Shared.Entities;
using Rts.Shared.Ids;

namespace Rts.Construction.Model;

/// <summary>Datos de un edificio existente, para validar <c>Build</c> en su forma "continuar" (D-032).</summary>
public sealed record ConstructionTargetInfo(EntityId BuildingId, PlayerId Owner, BuildingType BuildingType, bool IsCompleted);

/// <summary>
/// Datos de un edificio existente, para validar <c>RepairFarm</c> (03 §4). <see cref="IsDepleted"/> sale del estado
/// productivo de Economy; <see cref="IsRepairInProgress"/>, de si existe un <see cref="FarmRepairState"/>.
/// </summary>
public sealed record RepairTargetInfo(
    EntityId BuildingId,
    PlayerId Owner,
    BuildingType BuildingType,
    bool IsCompleted,
    bool IsDepleted,
    bool IsRepairInProgress);
