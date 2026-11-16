using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using static Rts.Combat.Tests.CombatTestData;

namespace Rts.Combat.Tests;

/// <summary>
/// Pruebas de daño agregado y simultaneidad lógica (contrato 02 §4, reglas 8 a 10): todo se calcula sobre el snapshot,
/// así que un atacante que muere en el tick igual ataca.
/// </summary>
public class DamageAggregationTests
{
  private const int OneHp = 1;

  [Fact]
  public void TwoAttackersOnOneTarget_ProduceOneAggregatedDamageAndOneDestruction()
  {
    CombatUnit a = Soldier(Soldier1, PlayerId.One, Field);
    CombatUnit b = Soldier(Soldier2, PlayerId.One, Field);
    CombatUnit target = Worker(Unit3, PlayerId.Two, Field, hp: Config.Soldier.Damage + OneHp);

    CombatTickResult result = Calculate(Input(units: [target, b, a], soldiers: [Attack(Soldier2, Unit3), Attack(Soldier1, Unit3)]));

    Assert.Equal(new[] { Soldier1, Soldier2 }, result.Attacks.Select(x => x.AttackerId));
    TargetDamage damage = Assert.Single(result.Damage);
    Assert.Equal(Config.Soldier.Damage * 2, damage.TotalDamage);
    Assert.Equal(0, damage.HpAfter);
    Assert.Equal(new EntityDestroyed(Unit3), Assert.Single(result.ProposedEvents));
  }

  [Fact]
  public void SurvivingTarget_GetsDamagedEventWithResultingHp()
  {
    CombatUnit attacker = Soldier(Soldier1, PlayerId.One, Field);
    CombatUnit target = Worker(Unit3, PlayerId.Two, Field, hp: Config.Soldier.Damage + OneHp);

    CombatTickResult result = Calculate(Input(units: [attacker, target], soldiers: [Attack(Soldier1, Unit3)]));

    Assert.Equal(new EntityDamaged(Unit3, OneHp), Assert.Single(result.ProposedEvents));
  }

  [Fact]
  public void SoldiersAttackingEachOther_CanKillEachOtherInTheSameTick()
  {
    CombatUnit p1 = Soldier(Soldier1, PlayerId.One, Field, hp: Config.Soldier.Damage);
    CombatUnit p2 = Soldier(Soldier2, PlayerId.Two, Field, hp: Config.Soldier.Damage);

    CombatTickResult result = Calculate(Input(units: [p2, p1], soldiers: [Attack(Soldier1, Soldier2), Attack(Soldier2, Soldier1)]));

    Assert.All(result.Damage, d => Assert.True(d.IsDestroyed));
    Assert.Equal(new IDomainEventPayload[] { new EntityDestroyed(Soldier1), new EntityDestroyed(Soldier2) }, result.ProposedEvents);
  }

  [Fact]
  public void TowerDestroyedThisTick_StillFires()
  {
    CombatBuilding tower = OperationalTower(PlayerId.One, hp: Config.Soldier.Damage);
    CombatUnit besieger = Soldier(Soldier2, PlayerId.Two, At(RightBorderX, MiddleZ), hp: Config.Tower.Damage);

    CombatTickResult result = Calculate(Input(
        units: [besieger], buildings: [tower], soldiers: [Attack(Soldier2, Tower20)], towers: [ReadyTower(Tower20)]));

    Assert.Equal(new[] { Soldier2, Tower20 }, result.Attacks.Select(x => x.AttackerId));
    Assert.Equal(new IDomainEventPayload[] { new EntityDestroyed(Soldier2), new EntityDestroyed(Tower20) }, result.ProposedEvents);
  }
}
