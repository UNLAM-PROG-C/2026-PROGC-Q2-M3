using Rts.Combat;
using Rts.Combat.Contracts;
using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Economy;
using Rts.Economy.Contracts;
using Rts.Production;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Composition;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Piezas de dominio que ninguna fase usa todavía (calculadores de Economy, Combat, Construction y Production, y reglas de
/// Production). Si alguna se llama, la prueba falla con un mensaje claro: hay que reemplazarla por un fake de verdad.
/// </summary>
internal sealed class UnusedModules :
    IEconomyCalculator, ICombatCalculator, IConstructionCalculator, IProductionCalculator, IProductionRules
{
  /// <summary>Módulos de prueba: los fakes que ya usan las fases implementadas, y el resto sin uso.</summary>
  public static DomainModules Create(FakeMovementRules? movementRules = null, FakeMovementCalculator? movementCalculator = null)
  {
    var unused = new UnusedModules();
    return new DomainModules(
        movementRules ?? new FakeMovementRules(), movementCalculator ?? new FakeMovementCalculator(),
        new FakeEconomyRules(), unused, new FakeCombatRules(), unused, new FakeConstructionRules(), unused, unused, unused);
  }

  public EconomyTickResult CalculateTick(EconomyTickInput input) => throw NotYet();

  public CombatTickResult CalculateTick(CombatTickInput input) => throw NotYet();

  public ConstructionTickResult CalculateTick(ConstructionTickInput input) => throw NotYet();

  public ProductionTickResult CalculateTick(ProductionTickInput input) => throw NotYet();

  public int GetTrainingDurationTicks(UnitType unitType) => throw NotYet();

  public ErrorCode? EvaluateTrainRequest(PlayerId requester, UnitType unitType, ProducerInfo? producer) => throw NotYet();

  public GridCell? FindSpawnCell(Footprint producerFootprint, SpawnView view, IReadOnlySet<GridCell> alreadyReserved) => throw NotYet();

  private static NotSupportedException NotYet() => new("Ninguna fase implementada usa esta pieza todavía.");
}
