using Rts.Combat.Configuration;
using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Combat.Calculations;

/// <summary>
/// Ataques de soldados con orden <c>Attack</c> (contrato 02 §4, reglas 1 a 4). Lee una entrada inmutable y no comparte
/// estado con <see cref="TowerCombatCalculator"/>, por eso pueden correr en paralelo. Los soldados sin orden
/// <c>Attack</c> no llegan acá (regla 4), así que nunca atacan.
/// </summary>
internal sealed class SoldierCombatCalculator : ISoldierCombatCalculation
{
  private readonly int _damage;
  private readonly double _rangeSquared;

  public SoldierCombatCalculator(CombatConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    _damage = config.Soldier.Damage;
    _rangeSquared = config.Soldier.Range * config.Soldier.Range;
  }

  public SoldierCombatOutcome Calculate(CombatTickInput input)
  {
    var targets = new TargetLookup(input);
    var attacks = new List<AttackPerformed>();
    var soldiersToIdle = new List<EntityId>();
    foreach (SoldierAttacker order in input.Soldiers.OrderBy(s => s.SoldierId))
    {
      ResolveOrder(input.Tick, order, targets, attacks, soldiersToIdle);
    }

    return new SoldierCombatOutcome(attacks.ToEquatableArray(), soldiersToIdle.ToEquatableArray());
  }

  /// <summary>
  /// Regla 1: objetivo inexistente o propio ⇒ el soldado va a Idle (caso defensivo: normalmente Simulation ya canceló la
  /// orden al confirmar la destrucción). Reglas 2 y 3: ataca solo si está listo y en rango (inclusivo); si no, no hay
  /// ataque ni cambio, porque la persecución es de Movement.
  /// </summary>
  private void ResolveOrder(
      TickId tick, SoldierAttacker order, TargetLookup targets, List<AttackPerformed> attacks, List<EntityId> soldiersToIdle)
  {
    CombatUnit soldier = targets.GetUnit(order.SoldierId);
    if (targets.ResolveTarget(order.TargetId, soldier.Position) is not { } target || target.Owner == soldier.Owner)
    {
      soldiersToIdle.Add(order.SoldierId);
      return;
    }

    if (AttackReadiness.IsReady(tick, order.ReadyAtTick) && target.DistanceSquared <= _rangeSquared)
    {
      attacks.Add(new AttackPerformed(order.SoldierId, order.TargetId, _damage));
    }
  }
}
