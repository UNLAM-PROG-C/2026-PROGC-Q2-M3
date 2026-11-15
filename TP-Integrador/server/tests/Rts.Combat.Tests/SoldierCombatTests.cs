using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using static Rts.Combat.Tests.CombatTestData;

namespace Rts.Combat.Tests;

/// <summary>Pruebas de ataques de soldados (contrato 02 §4, reglas 1 a 4; D-019, D-022).</summary>
public class SoldierCombatTests
{
  private static readonly CombatUnit Attacker = Soldier(Soldier1, PlayerId.One, Field);

  [Fact]
  public void SoldierWithoutAttackOrder_NeverAttacks()
  {
    CombatUnit adjacentEnemy = Worker(Unit3, PlayerId.Two, Attacker.Position);

    Assert.Equal(CombatTickResult.Empty(Now), Calculate(Input(units: [Attacker, adjacentEnemy])));
  }

  [Fact]
  public void TargetExactlyAtRange_IsAttacked_AndCooldownStarts()
  {
    CombatUnit enemy = Worker(Unit3, PlayerId.Two, At(Attacker.Position.X + SoldierRange, Attacker.Position.Z));

    CombatTickResult result = Calculate(Input(units: [Attacker, enemy], soldiers: [Attack(Soldier1, Unit3)]));

    Assert.Equal(new AttackPerformed(Soldier1, Unit3, Config.Soldier.Damage), Assert.Single(result.Attacks));
    Assert.Equal(new CooldownUpdate(Soldier1, Now.Plus(Config.Soldier.CooldownTicks)), Assert.Single(result.CooldownUpdates));
    Assert.Equal(enemy.Hp - Config.Soldier.Damage, Assert.Single(result.Damage).HpAfter);
  }

  [Fact]
  public void TargetJustBeyondRange_IsNotAttacked_AndOrderIsKept()
  {
    CombatUnit enemy = Worker(Unit3, PlayerId.Two, At(Attacker.Position.X + SoldierRange + JustOutside, Attacker.Position.Z));

    CombatTickResult result = Calculate(Input(units: [Attacker, enemy], soldiers: [Attack(Soldier1, Unit3)]));

    Assert.Equal(CombatTickResult.Empty(Now), result);
  }

  [Fact]
  public void SoldierNotReady_DoesNotAttack()
  {
    CombatUnit enemy = Worker(Unit3, PlayerId.Two, Attacker.Position);

    CombatTickResult result = Calculate(Input(units: [Attacker, enemy], soldiers: [Attack(Soldier1, Unit3, Now.Next())]));

    Assert.Equal(CombatTickResult.Empty(Now), result);
  }

  [Fact]
  public void CooldownBelongsToSoldier_ChangingTargetDoesNotResetIt()
  {
    CombatUnit[] units = [Attacker, Worker(Unit3, PlayerId.Two, Attacker.Position), Worker(Unit4, PlayerId.Two, Attacker.Position)];
    CombatTickResult firstAttack = Calculate(Input(units: [.. units], soldiers: [Attack(Soldier1, Unit3)]));
    TickId readyAt = Assert.Single(firstAttack.CooldownUpdates).ReadyAtTick;

    CombatTickResult tooSoon = Calculate(Input(units: [.. units], soldiers: [Attack(Soldier1, Unit4, readyAt)], tick: Now.Next()));
    CombatTickResult onTime = Calculate(Input(units: [.. units], soldiers: [Attack(Soldier1, Unit4, readyAt)], tick: readyAt));

    Assert.Empty(tooSoon.Attacks);
    Assert.Equal(Unit4, Assert.Single(onTime.Attacks).TargetId);
  }

  [Fact]
  public void MissingOrFriendlyTarget_SendsSoldierToIdle()
  {
    CombatUnit otherSoldier = Soldier(Soldier2, PlayerId.One, Attacker.Position);
    CombatUnit friend = Worker(Unit3, PlayerId.One, Attacker.Position);
    EntityId missing = Unit9;

    CombatTickResult result = Calculate(Input(
        units: [otherSoldier, Attacker, friend], soldiers: [Attack(Soldier2, Unit3), Attack(Soldier1, missing)]));

    Assert.Empty(result.Attacks);
    Assert.Equal(new[] { Soldier1, Soldier2 }, result.SoldiersToIdle);
  }

  [Fact]
  public void BuildingTarget_RangeIsMeasuredToTheFootprintBorder()
  {
    CombatUnit besieger = Soldier(Soldier1, PlayerId.One, At(RightBorderX + SoldierRange, MiddleZ));
    CombatBuilding barracks = Building(Building30, PlayerId.Two, BuildingType.Barracks, SturdyHp, TickId.Zero);

    CombatTickResult result = Calculate(Input(units: [besieger], buildings: [barracks], soldiers: [Attack(Soldier1, Building30)]));

    Assert.Equal(Building30, Assert.Single(result.Attacks).TargetId);
  }

  [Fact]
  public void ConstructionSite_LosesHp()
  {
    int siteHp = Config.Soldier.Damage + Config.Soldier.Damage;
    CombatUnit besieger = Soldier(Soldier1, PlayerId.One, At(RightBorderX, MiddleZ));
    CombatBuilding site = Building(Building30, PlayerId.Two, BuildingType.Barracks, siteHp, operationalFrom: null);

    CombatTickResult result = Calculate(Input(units: [besieger], buildings: [site], soldiers: [Attack(Soldier1, Building30)]));

    Assert.Equal(new EntityDamaged(Building30, siteHp - Config.Soldier.Damage), Assert.Single(result.ProposedEvents));
  }
}
