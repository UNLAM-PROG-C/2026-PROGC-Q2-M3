using Rts.Combat.Configuration;
using Rts.Shared.Entities;

namespace Rts.Combat.Tests;

public class CombatConfigTests
{
  /// <summary>Configuración válida SOLO para pruebas; los números son suposiciones temporales, no balance aprobado.</summary>
  internal static CombatConfig Valid() => new()
  {
    UnitMaxHp = new Dictionary<UnitType, int> { [UnitType.Worker] = 40, [UnitType.Soldier] = 100 },
    Soldier = new AttackStats(Damage: 10, Range: 1.5, CooldownTicks: 20),
    Tower = new AttackStats(Damage: 8, Range: 8.0, CooldownTicks: 30),
  };

  [Fact]
  public void ValidConfig_HasNoErrors()
  {
    Assert.Empty(Valid().GetValidationErrors());
    Valid().Validate();
  }

  [Fact]
  public void AttackStats_MustBePositive()
  {
    CombatConfig config = Valid() with
    {
      Soldier = new AttackStats(Damage: 0, Range: -1, CooldownTicks: 0),
      Tower = new AttackStats(Damage: 5, Range: double.PositiveInfinity, CooldownTicks: 10),
    };

    Assert.Equal(4, config.GetValidationErrors().Count);
    Assert.Throws<ArgumentException>(config.Validate);
  }

  [Fact]
  public void UnitMaxHp_MustCoverAllUnitTypesWithPositiveValues()
  {
    CombatConfig missing = Valid() with { UnitMaxHp = new Dictionary<UnitType, int> { [UnitType.Worker] = 40 } };
    CombatConfig zero = Valid() with
    {
      UnitMaxHp = new Dictionary<UnitType, int> { [UnitType.Worker] = 0, [UnitType.Soldier] = 100 },
    };

    Assert.Contains(missing.GetValidationErrors(), e => e.Contains(nameof(UnitType.Soldier)));
    Assert.Single(zero.GetValidationErrors());
  }
}
