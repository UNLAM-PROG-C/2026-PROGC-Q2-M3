using Rts.Combat.Contracts;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Combat.Calculations;

/// <summary>
/// Agregación del daño del tick (contrato 02 §4, reglas 8 a 10). Todos los ataques se calcularon sobre el mismo snapshot,
/// así que un atacante que muere en este tick igual cuenta: eso permite muertes mutuas y que una torre destruida dispare.
/// </summary>
internal static class DamageAggregator
{
  private const int NoDamage = 0;

  /// <summary>
  /// Regla 9: suma el daño de todos los atacantes de cada objetivo en un único <see cref="TargetDamage"/>, que calcula el
  /// HP resultante una sola vez y con piso cero. Así un objetivo recibe a lo sumo una destrucción. Orden por TargetId.
  /// </summary>
  public static EquatableArray<TargetDamage> Aggregate(IEnumerable<AttackPerformed> attacks, CombatTickInput input)
  {
    Dictionary<EntityId, int> hpBefore = CurrentHpById(input);
    return attacks
        .GroupBy(a => a.TargetId)
        .OrderBy(g => g.Key)
        .Select(g => new TargetDamage(g.Key, hpBefore[g.Key], TotalDamage(g)))
        .ToEquatableArray();
  }

  /// <summary>Regla 10: por objetivo, <c>EntityDamaged</c> si sobrevive o <c>EntityDestroyed</c> si llega a 0.</summary>
  public static EquatableArray<IDomainEventPayload> ToEvents(EquatableArray<TargetDamage> damage) => damage
      .Select(d => d.IsDestroyed
          ? (IDomainEventPayload)new EntityDestroyed(d.TargetId)
          : new EntityDamaged(d.TargetId, d.HpAfter))
      .ToEquatableArray();

  private static int TotalDamage(IEnumerable<AttackPerformed> attacks) =>
      attacks.Aggregate(NoDamage, (total, attack) => checked(total + attack.Damage));

  private static Dictionary<EntityId, int> CurrentHpById(CombatTickInput input) => input.Units
      .Select(u => (u.Id, u.Hp))
      .Concat(input.Buildings.Select(b => (b.Id, b.Hp)))
      .ToDictionary(e => e.Id, e => e.Hp);
}
