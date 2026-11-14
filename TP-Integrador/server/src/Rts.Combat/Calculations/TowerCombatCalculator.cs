using Rts.Combat.Configuration;
using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Collections;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Combat.Calculations;

/// <summary>
/// Autoataques de torres (contrato 02 §4, reglas 5 a 7). Lee una entrada inmutable y no comparte estado con
/// <see cref="SoldierCombatCalculator"/>, por eso pueden correr en paralelo.
/// </summary>
internal sealed class TowerCombatCalculator : ITowerCombatCalculation
{
  private readonly int _damage;
  private readonly double _rangeSquared;

  public TowerCombatCalculator(CombatConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    _damage = config.Tower.Damage;
    _rangeSquared = config.Tower.Range * config.Tower.Range;
  }

  public EquatableArray<AttackPerformed> Calculate(CombatTickInput input)
  {
    Dictionary<EntityId, CombatBuilding> buildings = input.Buildings.ToDictionary(b => b.Id);
    var attacks = new List<AttackPerformed>();
    foreach (TowerAttacker tower in input.Towers.OrderBy(t => t.TowerId))
    {
      CombatBuilding building = buildings[tower.TowerId];
      if (CanFire(input.Tick, tower, building) && FindNearestEnemy(input, building) is { } targetId)
      {
        attacks.Add(new AttackPerformed(tower.TowerId, targetId, _damage));
      }
    }

    return attacks.ToEquatableArray();
  }

  /// <summary>
  /// Regla 5 (D-024): solo actúa si es operativa en este tick; una torre completada en N recién ataca desde N+1.
  /// Además tiene que estar lista (D-022).
  /// </summary>
  private static bool CanFire(TickId tick, TowerAttacker tower, CombatBuilding building) =>
      building.IsOperationalAt(tick) && AttackReadiness.IsReady(tick, tower.ReadyAtTick);

  /// <summary>
  /// Reglas 6 y 7: candidatos son solo unidades enemigas (nunca edificios) a distancia del borde de la huella menor o
  /// igual al rango. Elige la más cercana; ante igualdad exacta de distancia al cuadrado, la de menor <c>EntityId</c>.
  /// </summary>
  private EntityId? FindNearestEnemy(CombatTickInput input, CombatBuilding tower)
  {
    WorldRect towerArea = input.Grid.ToWorldRect(tower.Footprint);
    return input.Units
        .Where(u => u.Owner != tower.Owner)
        .Select(u => (u.Id, DistanceSquared: towerArea.DistanceSquaredTo(u.Position)))
        .Where(c => c.DistanceSquared <= _rangeSquared)
        .OrderBy(c => c.DistanceSquared)
        .ThenBy(c => c.Id)
        .Select(c => (EntityId?)c.Id)
        .FirstOrDefault();
  }
}
