using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Combat.Contracts;

/// <summary>
/// Propuesta inmutable de Combat para un tick (02 §6). Simulation la valida y la confirma después de la barrera
/// Economy ∥ Combat; Combat no modifica HP en el estado oficial. Las colecciones vienen en orden determinista
/// (contrato 02 §5).
/// </summary>
/// <param name="Tick">Tick de la entrada que originó la propuesta.</param>
/// <param name="Attacks">Ataques efectuados, uno por atacante (por AttackerId ascendente).</param>
/// <param name="Damage">Daño agregado por objetivo, con el HP resultante y si se destruye (por TargetId ascendente).</param>
/// <param name="CooldownUpdates">Nuevo <c>ReadyAtTick</c> de cada atacante que atacó (por AttackerId ascendente).</param>
/// <param name="SoldiersToIdle">Soldados cuyo objetivo ya no es válido en el snapshot (por ID ascendente).</param>
/// <param name="ProposedEvents">Eventos propuestos (<c>EntityDamaged</c>, <c>EntityDestroyed</c>) en orden de TargetId.</param>
public sealed record CombatTickResult(
    TickId Tick,
    EquatableArray<AttackPerformed> Attacks,
    EquatableArray<TargetDamage> Damage,
    EquatableArray<CooldownUpdate> CooldownUpdates,
    EquatableArray<EntityId> SoldiersToIdle,
    EquatableArray<IDomainEventPayload> ProposedEvents)
{
  /// <summary>Resultado sin cambios para el tick indicado.</summary>
  public static CombatTickResult Empty(TickId tick) => new(tick, default, default, default, default, default);
}

/// <summary>Un ataque calculado sobre el snapshot. Cuenta aunque el atacante muera en este mismo tick (02 §7).</summary>
public sealed record AttackPerformed(EntityId AttackerId, EntityId TargetId, int Damage);

/// <summary>Nuevo instante en que el atacante vuelve a estar listo: tick del ataque + cooldown de su tipo.</summary>
public sealed record CooldownUpdate(EntityId AttackerId, TickId ReadyAtTick);

/// <summary>
/// Daño total recibido por un objetivo en el tick. <see cref="HpAfter"/> y <see cref="IsDestroyed"/> se derivan
/// en el constructor: HP con piso cero, aplicado una sola vez (02 §3). Varios atacantes ⇒ una sola destrucción.
/// </summary>
public sealed record TargetDamage
{
  public TargetDamage(EntityId targetId, int hpBefore, int totalDamage)
  {
    if (targetId.IsNone)
    {
      throw new ArgumentException("El objetivo debe tener un EntityId válido.", nameof(targetId));
    }

    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hpBefore);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalDamage);

    TargetId = targetId;
    HpBefore = hpBefore;
    TotalDamage = totalDamage;
    HpAfter = Math.Max(0, hpBefore - totalDamage);
  }

  public EntityId TargetId { get; }

  public int HpBefore { get; }

  public int TotalDamage { get; }

  public int HpAfter { get; }

  public bool IsDestroyed => HpAfter == 0;
}
