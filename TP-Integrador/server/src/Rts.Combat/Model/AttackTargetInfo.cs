using Rts.Shared.Entities;
using Rts.Shared.Ids;

namespace Rts.Combat.Model;

/// <summary>
/// Datos mínimos del objetivo de una orden <c>Attack</c>, para validarla al procesar el comando (fase 1).
/// <see cref="Owner"/> es nulo para entidades sin dueño (árboles).
/// </summary>
public sealed record AttackTargetInfo(EntityId Id, EntityKind Kind, PlayerId? Owner);
