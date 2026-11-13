using Rts.Simulation.Composition;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Los diez módulos de dominio armados con fakes, para las pruebas que usan la fábrica. Cada parámetro permite reemplazar un fake
/// por uno configurado en la prueba; los que no se pasan van con su comportamiento por defecto.
/// </summary>
internal static class TestModules
{
  public static DomainModules Create(
      FakeMovementRules? movementRules = null,
      FakeMovementCalculator? movementCalculator = null,
      FakeEconomyCalculator? economyCalculator = null,
      FakeCombatCalculator? combatCalculator = null,
      FakeConstructionCalculator? constructionCalculator = null,
      FakeProductionRules? productionRules = null,
      FakeProductionCalculator? productionCalculator = null) =>
      new(
          movementRules ?? new FakeMovementRules(), movementCalculator ?? new FakeMovementCalculator(),
          new FakeEconomyRules(), economyCalculator ?? new FakeEconomyCalculator(),
          new FakeCombatRules(), combatCalculator ?? new FakeCombatCalculator(),
          new FakeConstructionRules(), constructionCalculator ?? new FakeConstructionCalculator(),
          productionRules ?? new FakeProductionRules(), productionCalculator ?? new FakeProductionCalculator());
}
