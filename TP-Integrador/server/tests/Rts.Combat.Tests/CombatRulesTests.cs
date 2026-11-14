using Rts.Combat.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using static Rts.Combat.Tests.CombatTestData;

namespace Rts.Combat.Tests;

/// <summary>Pruebas de <see cref="CombatRules"/>: HP máximo de unidades y la tabla de validación de Attack (02 §6).</summary>
public class CombatRulesTests
{
  private static readonly CombatRules Rules = new(Config);

  [Fact]
  public void UnitMaxHp_ComesFromConfiguration()
  {
    foreach ((UnitType type, int maxHp) in Config.UnitMaxHp)
    {
      Assert.Equal(maxHp, Rules.GetUnitMaxHp(type));
    }
  }

  [Fact]
  public void WorkerCannotAttack()
  {
    var enemy = new AttackTargetInfo(Unit3, EntityKind.Unit, PlayerId.Two);

    Assert.Equal(CommonErrors.IncompatibleEntity, Rules.ValidateAttackTarget(PlayerId.One, UnitType.Worker, enemy));
  }

  [Fact]
  public void MissingTarget_IsNotFound()
  {
    Assert.Equal(CommonErrors.EntityNotFound, Rules.ValidateAttackTarget(PlayerId.One, UnitType.Soldier, null));
  }

  [Fact]
  public void Tree_IsNotAValidTarget()
  {
    var tree = new AttackTargetInfo(Unit3, EntityKind.Tree, null);

    Assert.Equal(CombatErrors.InvalidTarget, Rules.ValidateAttackTarget(PlayerId.One, UnitType.Soldier, tree));
  }

  [Fact]
  public void OwnUnitOrBuilding_IsFriendly()
  {
    var ownUnit = new AttackTargetInfo(Unit3, EntityKind.Unit, PlayerId.One);
    var ownBuilding = new AttackTargetInfo(Building30, EntityKind.Building, PlayerId.One);

    Assert.Equal(CombatErrors.FriendlyTarget, Rules.ValidateAttackTarget(PlayerId.One, UnitType.Soldier, ownUnit));
    Assert.Equal(CombatErrors.FriendlyTarget, Rules.ValidateAttackTarget(PlayerId.One, UnitType.Soldier, ownBuilding));
  }

  [Fact]
  public void EnemyUnitOrBuilding_IsValid()
  {
    var enemyUnit = new AttackTargetInfo(Unit3, EntityKind.Unit, PlayerId.Two);
    var enemyBuilding = new AttackTargetInfo(Building30, EntityKind.Building, PlayerId.Two);

    Assert.Null(Rules.ValidateAttackTarget(PlayerId.One, UnitType.Soldier, enemyUnit));
    Assert.Null(Rules.ValidateAttackTarget(PlayerId.One, UnitType.Soldier, enemyBuilding));
  }
}
