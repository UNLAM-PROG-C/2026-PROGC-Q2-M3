using Rts.Shared.Entities;
using Rts.Shared.Ids;

namespace Rts.Production.Model;

/// <summary>
/// Datos de un edificio existente para validar un <c>TrainUnit</c> en la fase 1 (05 §1-2).
/// <see cref="IsOperational"/>: completado y operativo en el tick actual. <see cref="PendingCount"/>: tamaño actual de su cola,
/// sin contar la tarea activa (incluye lo encolado antes en el mismo lote).
/// </summary>
public sealed record ProducerInfo(EntityId BuildingId, PlayerId Owner, BuildingType BuildingType, bool IsOperational, int PendingCount);
