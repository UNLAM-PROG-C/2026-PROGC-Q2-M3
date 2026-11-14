using Rts.Combat.Calculations;
using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using static Rts.Combat.Tests.CombatTestData;

namespace Rts.Combat.Tests;

/// <summary>
/// Determinismo de Combat (contrato 02 §8): el resultado no depende del orden de las colecciones de entrada ni de qué
/// subcálculo (soldados o torres) termina primero cuando corren en paralelo.
/// </summary>
public class CombatDeterminismTests
{
  [Fact]
  public void Result_DoesNotDependOnInputOrder()
  {
    Assert.Equal(Calculate(BusyTick(reversed: false)), Calculate(BusyTick(reversed: true)));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Result_DoesNotDependOnWhichSubcalculationFinishesFirst(bool soldiersFinishFirst)
  {
    CombatTickInput input = BusyTick(reversed: false);
    var order = new CompletionOrder();
    var calculator = new CombatCalculator(
        new GatedSoldiers(new SoldierCombatCalculator(Config), order, soldiersFinishFirst),
        new GatedTowers(new TowerCombatCalculator(Config), order, !soldiersFinishFirst),
        Config,
        runSubcalculationsInParallel: true);

    CombatTickResult result = calculator.CalculateTick(input);

    Assert.Equal(ExpectedFinishOrder(soldiersFinishFirst), order.Finished);
    Assert.Equal(Calculate(input), result);
  }

  [Fact]
  public void ParallelAndSequentialModes_GiveTheSameResult()
  {
    CombatTickInput input = BusyTick(reversed: false);

    Assert.Equal(Calculate(input), new CombatCalculator(Config, runSubcalculationsInParallel: true).CalculateTick(input));
  }

  [Fact]
  public void SoldierAndTowerDamageOnTheSameTarget_AreAggregatedTogether()
  {
    TargetDamage damage = Assert.Single(Calculate(BusyTick(reversed: false)).Damage, d => d.TargetId == Soldier2);

    Assert.Equal(Config.Soldier.Damage + Config.Tower.Damage, damage.TotalDamage);
  }

  /// <summary>
  /// Un tick con trabajo en los dos subcálculos: el soldado de P1 y la torre de P1 atacan al mismo soldado de P2, que a su
  /// vez ataca a la torre. Hay daño de ambos subcálculos sobre un mismo objetivo, así que la consolidación importa.
  /// </summary>
  private static CombatTickInput BusyTick(bool reversed)
  {
    PositionXZ besidesTower = At(RightBorderX, MiddleZ);
    CombatUnit[] units = [Soldier(Soldier1, PlayerId.One, besidesTower), Soldier(Soldier2, PlayerId.Two, besidesTower)];
    SoldierAttacker[] orders = [Attack(Soldier1, Soldier2), Attack(Soldier2, Tower20)];
    CombatBuilding[] buildings = [OperationalTower(PlayerId.One, hp: Config.Soldier.Damage + Config.Soldier.Damage)];
    return reversed
        ? Input(units: [.. units.Reverse()], buildings: [.. buildings], soldiers: [.. orders.Reverse()], towers: [ReadyTower(Tower20)])
        : Input(units: [.. units], buildings: [.. buildings], soldiers: [.. orders], towers: [ReadyTower(Tower20)]);
  }

  private static string[] ExpectedFinishOrder(bool soldiersFinishFirst) => soldiersFinishFirst
      ? [GatedSoldiers.Name, GatedTowers.Name]
      : [GatedTowers.Name, GatedSoldiers.Name];
}
