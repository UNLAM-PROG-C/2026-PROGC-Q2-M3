using Rts.Combat;
using Rts.Construction;
using Rts.Economy;
using Rts.Movement;
using Rts.Production;

namespace Rts.Simulation.Composition;

/// <summary>
/// Las implementaciones de los cinco módulos de dominio que usa el motor. Cada módulo aporta dos piezas:
/// <list type="bullet">
/// <item><b>Reglas</b> (<c>I*Rules</c>): consultas puntuales, sobre todo para validar comandos en la fase 1.</item>
/// <item><b>Calculador</b> (<c>I*Calculator</c>): el cálculo de su fase del tick, como función pura (entrada → propuesta).</item>
/// </list>
/// Simulation solo conoce estas interfaces; la composición del servidor le pasa las implementaciones reales y las pruebas, fakes.
/// </summary>
public sealed record DomainModules
{
  public DomainModules(
      IMovementRules movementRules,
      IMovementCalculator movementCalculator,
      IEconomyRules economyRules,
      IEconomyCalculator economyCalculator,
      ICombatRules combatRules,
      ICombatCalculator combatCalculator,
      IConstructionRules constructionRules,
      IConstructionCalculator constructionCalculator,
      IProductionRules productionRules,
      IProductionCalculator productionCalculator)
  {
    MovementRules = movementRules ?? throw new ArgumentNullException(nameof(movementRules));
    MovementCalculator = movementCalculator ?? throw new ArgumentNullException(nameof(movementCalculator));
    EconomyRules = economyRules ?? throw new ArgumentNullException(nameof(economyRules));
    EconomyCalculator = economyCalculator ?? throw new ArgumentNullException(nameof(economyCalculator));
    CombatRules = combatRules ?? throw new ArgumentNullException(nameof(combatRules));
    CombatCalculator = combatCalculator ?? throw new ArgumentNullException(nameof(combatCalculator));
    ConstructionRules = constructionRules ?? throw new ArgumentNullException(nameof(constructionRules));
    ConstructionCalculator = constructionCalculator ?? throw new ArgumentNullException(nameof(constructionCalculator));
    ProductionRules = productionRules ?? throw new ArgumentNullException(nameof(productionRules));
    ProductionCalculator = productionCalculator ?? throw new ArgumentNullException(nameof(productionCalculator));
  }

  public IMovementRules MovementRules { get; }

  public IMovementCalculator MovementCalculator { get; }

  public IEconomyRules EconomyRules { get; }

  public IEconomyCalculator EconomyCalculator { get; }

  public ICombatRules CombatRules { get; }

  public ICombatCalculator CombatCalculator { get; }

  public IConstructionRules ConstructionRules { get; }

  public IConstructionCalculator ConstructionCalculator { get; }

  public IProductionRules ProductionRules { get; }

  public IProductionCalculator ProductionCalculator { get; }
}
