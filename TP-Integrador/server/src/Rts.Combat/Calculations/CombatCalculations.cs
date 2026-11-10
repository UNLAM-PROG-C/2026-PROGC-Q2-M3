using Rts.Combat.Contracts;
using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Combat.Calculations;

/// <summary>
/// Subcálculo interno de ataques de soldados (02 §6: <c>SoldierCombatCalculation</c>). Puede ejecutarse en paralelo con
/// <see cref="ITowerCombatCalculation"/>; ambos leen la misma entrada inmutable y no comparten estado mutable.
/// </summary>
internal interface ISoldierCombatCalculation
{
  SoldierCombatOutcome Calculate(CombatTickInput input);
}

/// <summary>
/// Subcálculo interno de autoataques de torres (02 §6: <c>TowerCombatCalculation</c>): operatividad, enemigo más cercano
/// con desempate por menor ID, cooldown.
/// </summary>
internal interface ITowerCombatCalculation
{
  EquatableArray<AttackPerformed> Calculate(CombatTickInput input);
}

/// <summary>Resultado parcial de los soldados: ataques efectuados y soldados con objetivo inválido.</summary>
internal sealed record SoldierCombatOutcome(
    EquatableArray<AttackPerformed> Attacks,
    EquatableArray<EntityId> SoldiersToIdle);
