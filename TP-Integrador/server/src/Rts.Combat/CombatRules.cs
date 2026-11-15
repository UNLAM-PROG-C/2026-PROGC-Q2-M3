using System.Collections.Immutable;
using Rts.Combat.Configuration;
using Rts.Combat.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Combat;

/// <summary>
/// Reglas de combate que Simulation consulta fuera del cálculo del tick (contrato 02 §1 y §6). La instancia es inmutable,
/// así que es segura para uso concurrente.
/// </summary>
public sealed class CombatRules : ICombatRules
{
  private readonly ImmutableDictionary<UnitType, int> _unitMaxHp;

  public CombatRules(CombatConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();

    // Copia inmutable: la tabla de la configuración podría modificarse desde afuera después de construir esto.
    _unitMaxHp = config.UnitMaxHp.ToImmutableDictionary();
  }

  /// <summary>HP máximo de un tipo de unidad (D-021). Simulation lo usa al confirmar una aparición.</summary>
  /// <exception cref="ArgumentOutOfRangeException">Si el tipo no tiene HP configurado.</exception>
  public int GetUnitMaxHp(UnitType unitType)
  {
    if (!_unitMaxHp.TryGetValue(unitType, out int maxHp))
    {
      throw new ArgumentOutOfRangeException(nameof(unitType), unitType, "Tipo de unidad sin HP máximo configurado.");
    }

    return maxHp;
  }

  /// <summary>
  /// Valida el objetivo de una orden <c>Attack</c> en la fase 1 (contrato 02 §6), en el orden de la tabla del contrato.
  /// La propiedad del atacante ya la verificó Simulation y la alcanzabilidad la decide Movement.
  /// </summary>
  public ErrorCode? ValidateAttackTarget(PlayerId attackerOwner, UnitType attackerType, AttackTargetInfo? target)
  {
    if (attackerType != UnitType.Soldier)
    {
      return CommonErrors.IncompatibleEntity;
    }

    if (target is null)
    {
      return CommonErrors.EntityNotFound;
    }

    return ValidateKnownTarget(attackerOwner, target);
  }

  /// <summary>Un árbol no es objetivo; una unidad o un edificio propio tampoco. Una obra enemiga sí es válida.</summary>
  private static ErrorCode? ValidateKnownTarget(PlayerId attackerOwner, AttackTargetInfo target)
  {
    if (target.Kind is not (EntityKind.Unit or EntityKind.Building))
    {
      return CombatErrors.InvalidTarget;
    }

    return target.Owner == attackerOwner ? CombatErrors.FriendlyTarget : null;
  }
}
