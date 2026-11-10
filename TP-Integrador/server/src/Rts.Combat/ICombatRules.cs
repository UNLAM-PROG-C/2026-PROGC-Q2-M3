using Rts.Combat.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Combat;

/// <summary>
/// Reglas de combate que Simulation consulta fuera del cálculo del tick. Implementaciones puras y seguras para uso concurrente.
/// </summary>
public interface ICombatRules
{
  /// <summary>HP máximo de un tipo de unidad (D-021); Simulation lo usa al confirmar un spawn.</summary>
  int GetUnitMaxHp(UnitType unitType);

  /// <summary>
  /// Valida el objetivo de una orden <c>Attack</c> al procesar el comando (02 §6, <c>ValidateAttackTarget</c>).
  /// Devuelve <c>null</c> si es válido, o el motivo del rechazo:
  /// <list type="bullet">
  /// <item><c>Common.IncompatibleEntity</c>: el atacante no es soldado (los trabajadores no atacan).</item>
  /// <item><c>Common.EntityNotFound</c>: el objetivo no existe (<paramref name="target"/> nulo).</item>
  /// <item><c>Combat.InvalidTarget</c>: el objetivo no es unidad ni edificio.</item>
  /// <item><c>Combat.FriendlyTarget</c>: el objetivo es propio.</item>
  /// </list>
  /// La propiedad del atacante (<c>Common.NotOwner</c>) la valida Simulation de forma general, antes.
  /// La alcanzabilidad la decide Units / Movement.
  /// </summary>
  ErrorCode? ValidateAttackTarget(PlayerId attackerOwner, UnitType attackerType, AttackTargetInfo? target);
}
