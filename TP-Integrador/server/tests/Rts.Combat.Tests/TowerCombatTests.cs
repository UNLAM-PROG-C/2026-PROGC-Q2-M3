using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using static Rts.Combat.Tests.CombatTestData;

namespace Rts.Combat.Tests;

/// <summary>Pruebas de autoataques de torres (contrato 02 §4, reglas 5 a 7; D-020, D-024).</summary>
public class TowerCombatTests
{
  private const double Near = 1.0;
  private const double Far = 3.0;

  private static readonly CombatBuilding Tower = OperationalTower(PlayerId.One, hp: Config.Soldier.Damage);

  [Fact]
  public void Tower_TargetsTheNearestEnemyUnit()
  {
    CombatUnit far = Worker(Unit3, PlayerId.Two, At(RightBorderX + Far, MiddleZ));
    CombatUnit near = Worker(Unit4, PlayerId.Two, At(RightBorderX + Near, MiddleZ));

    CombatTickResult result = Calculate(Input(units: [far, near], buildings: [Tower], towers: [ReadyTower(Tower20)]));

    Assert.Equal(new AttackPerformed(Tower20, Unit4, Config.Tower.Damage), Assert.Single(result.Attacks));
    Assert.Equal(new CooldownUpdate(Tower20, Now.Plus(Config.Tower.CooldownTicks)), Assert.Single(result.CooldownUpdates));
  }

  [Fact]
  public void ExactTie_IsBrokenBySmallestEntityId()
  {
    CombatUnit higherId = Worker(Unit9, PlayerId.Two, At(RightBorderX + Near, MiddleZ));
    CombatUnit lowerId = Worker(Unit3, PlayerId.Two, At(RightBorderX + Near, MiddleZ));

    CombatTickResult result = Calculate(Input(units: [higherId, lowerId], buildings: [Tower], towers: [ReadyTower(Tower20)]));

    Assert.Equal(Unit3, Assert.Single(result.Attacks).TargetId);
  }

  [Fact]
  public void RangeIsMeasuredFromTheFootprintBorder()
  {
    CombatUnit atRange = Worker(Unit3, PlayerId.Two, At(RightBorderX + TowerRange, MiddleZ));
    CombatUnit beyond = Worker(Unit3, PlayerId.Two, At(RightBorderX + TowerRange + JustOutside, MiddleZ));

    Assert.Single(Calculate(Input(units: [atRange], buildings: [Tower], towers: [ReadyTower(Tower20)])).Attacks);
    Assert.Empty(Calculate(Input(units: [beyond], buildings: [Tower], towers: [ReadyTower(Tower20)])).Attacks);
  }

  [Fact]
  public void Tower_IgnoresFriendlyUnitsAndNeverTargetsBuildings()
  {
    CombatUnit friend = Worker(Unit3, PlayerId.One, At(RightBorderX + Near, MiddleZ));
    var farmFootprint = new Footprint(NextToAnchor, TwoByTwo);
    CombatBuilding enemyFarm = new(Building30, PlayerId.Two, BuildingType.Farm, farmFootprint, SturdyHp, TickId.Zero);

    CombatTickResult result = Calculate(Input(units: [friend], buildings: [Tower, enemyFarm], towers: [ReadyTower(Tower20)]));

    Assert.Empty(result.Attacks);
  }

  [Fact]
  public void TowerCompletedThisTick_StartsAttackingNextTick()
  {
    CombatUnit enemy = Worker(Unit3, PlayerId.Two, At(RightBorderX + Near, MiddleZ));
    CombatBuilding justCompleted = Building(Tower20, PlayerId.One, BuildingType.Tower, Config.Soldier.Damage, operationalFrom: Now.Next());

    CombatTickInput completionTick = Input(units: [enemy], buildings: [justCompleted], towers: [ReadyTower(Tower20)]);
    CombatTickInput nextTick = Input(units: [enemy], buildings: [justCompleted], towers: [ReadyTower(Tower20)], tick: Now.Next());

    Assert.Empty(Calculate(completionTick).Attacks);
    Assert.Single(Calculate(nextTick).Attacks);
  }

  [Fact]
  public void TowerNotReady_DoesNotAttack()
  {
    CombatUnit enemy = Worker(Unit3, PlayerId.Two, At(RightBorderX + Near, MiddleZ));

    CombatTickResult result = Calculate(Input(units: [enemy], buildings: [Tower], towers: [new TowerAttacker(Tower20, Now.Next())]));

    Assert.Empty(result.Attacks);
  }
}
