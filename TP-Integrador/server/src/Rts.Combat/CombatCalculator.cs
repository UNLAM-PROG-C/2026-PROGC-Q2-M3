using Rts.Combat.Calculations;
using Rts.Combat.Configuration;
using Rts.Combat.Contracts;
using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Combat;

/// <summary>
/// Cálculo de combate de un tick (contrato 02). Simulation lo invoca en la fase 5, en paralelo con Economy y sobre el
/// mismo snapshot. Ejecuta los subcálculos de soldados y torres y consolida en un orden fijo, así que el resultado no
/// depende de cuál termina primero. No modifica HP: devuelve una propuesta que Simulation confirma en la fase 6.
/// </summary>
public sealed class CombatCalculator : ICombatCalculator
{
  private readonly ISoldierCombatCalculation _soldiers;
  private readonly ITowerCombatCalculation _towers;
  private readonly int _soldierCooldownTicks;
  private readonly int _towerCooldownTicks;
  private readonly bool _runSubcalculationsInParallel;

  /// <param name="config">Configuración de combate; se valida acá.</param>
  /// <param name="runSubcalculationsInParallel">
  /// Si es verdadero, soldados y torres se calculan en paralelo. Por defecto es falso: ver
  /// <see cref="SubcalculationRunner"/> sobre la medición de D-051.
  /// </param>
  public CombatCalculator(CombatConfig config, bool runSubcalculationsInParallel = false)
      : this(new SoldierCombatCalculator(Validated(config)), new TowerCombatCalculator(config), config, runSubcalculationsInParallel)
  {
  }

  /// <summary>
  /// Recibe los subcálculos ya construidos. Lo usan las pruebas para reemplazarlos por versiones que fuerzan un orden de
  /// finalización (contrato 02 §8).
  /// </summary>
  internal CombatCalculator(
      ISoldierCombatCalculation soldiers, ITowerCombatCalculation towers, CombatConfig config, bool runSubcalculationsInParallel)
  {
    ArgumentNullException.ThrowIfNull(soldiers);
    ArgumentNullException.ThrowIfNull(towers);
    ArgumentNullException.ThrowIfNull(config);
    (_soldiers, _towers) = (soldiers, towers);
    _soldierCooldownTicks = config.Soldier.CooldownTicks;
    _towerCooldownTicks = config.Tower.CooldownTicks;
    _runSubcalculationsInParallel = runSubcalculationsInParallel;
  }

  public CombatTickResult CalculateTick(CombatTickInput input)
  {
    ArgumentNullException.ThrowIfNull(input);
    (SoldierCombatOutcome soldiers, EquatableArray<AttackPerformed> towers) = SubcalculationRunner.Run(
        _runSubcalculationsInParallel, () => _soldiers.Calculate(input), () => _towers.Calculate(input));
    return Consolidate(input, soldiers, towers);
  }

  /// <summary>
  /// Regla 11: junta los ataques de soldados y torres y ordena todo por ID ascendente. Cada atacante ataca a lo sumo una
  /// vez por tick, así que <c>AttackerId</c> identifica cada ataque.
  /// </summary>
  private CombatTickResult Consolidate(CombatTickInput input, SoldierCombatOutcome soldiers, EquatableArray<AttackPerformed> towers)
  {
    AttackPerformed[] attacks = [.. soldiers.Attacks.Concat(towers).OrderBy(a => a.AttackerId)];
    EquatableArray<TargetDamage> damage = DamageAggregator.Aggregate(attacks, input);
    return new CombatTickResult(
        input.Tick,
        attacks.ToEquatableArray(),
        damage,
        CooldownUpdatesFor(input, attacks),
        soldiers.SoldiersToIdle.Order().ToEquatableArray(),
        DamageAggregator.ToEvents(damage));
  }

  /// <summary>
  /// D-022: cada atacante que atacó vuelve a estar listo en <c>Tick + CooldownTicks</c> de su tipo. Los IDs son únicos en
  /// todo el mundo, así que pertenecer a <c>Soldiers</c> alcanza para saber que es un soldado.
  /// </summary>
  private EquatableArray<CooldownUpdate> CooldownUpdatesFor(CombatTickInput input, AttackPerformed[] attacks)
  {
    HashSet<EntityId> soldierIds = [.. input.Soldiers.Select(s => s.SoldierId)];
    return attacks
        .Select(a => new CooldownUpdate(a.AttackerId, AttackReadiness.NextReadyTick(input.Tick, CooldownOf(a.AttackerId, soldierIds))))
        .ToEquatableArray();
  }

  private int CooldownOf(EntityId attackerId, HashSet<EntityId> soldierIds) =>
      soldierIds.Contains(attackerId) ? _soldierCooldownTicks : _towerCooldownTicks;

  private static CombatConfig Validated(CombatConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    return config;
  }
}
