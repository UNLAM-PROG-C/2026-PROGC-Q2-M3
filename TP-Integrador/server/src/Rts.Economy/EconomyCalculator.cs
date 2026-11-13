using Rts.Economy.Calculations;
using Rts.Economy.Configuration;
using Rts.Economy.Contracts;
using Rts.Shared.Collections;
using Rts.Shared.Ids;
using Rts.Shared.Resources;

namespace Rts.Economy;

/// <summary>
/// Cálculo económico de un tick (contrato 01). Simulation lo invoca en la fase 5, en paralelo con Combat y sobre el mismo
/// snapshot. Ejecuta los dos subcálculos y consolida sus resultados en un orden fijo, de modo que el resultado no depende
/// de cuál termina primero. No escribe <c>WorldState</c>: devuelve una propuesta que Simulation confirma en la fase 6.
/// </summary>
public sealed class EconomyCalculator : IEconomyCalculator
{
  private readonly IGatheringCalculation _gathering;
  private readonly IFarmProductionCalculation _farmProduction;
  private readonly bool _runSubcalculationsInParallel;

  /// <param name="config">Configuración económica; se valida acá.</param>
  /// <param name="runSubcalculationsInParallel">
  /// Si es verdadero, Gathering y FarmProduction corren en paralelo. Por defecto es falso: ver
  /// <see cref="SubcalculationRunner"/> sobre la medición de D-051.
  /// </param>
  public EconomyCalculator(EconomyConfig config, bool runSubcalculationsInParallel = false)
      : this(new GatheringCalculator(Validated(config)), new FarmProductionCalculator(config), runSubcalculationsInParallel)
  {
  }

  /// <summary>
  /// Recibe los subcálculos ya construidos. Lo usan las pruebas para reemplazarlos por versiones que fuerzan un orden de
  /// finalización (contrato 01 §7).
  /// </summary>
  internal EconomyCalculator(IGatheringCalculation gathering, IFarmProductionCalculation farmProduction, bool runSubcalculationsInParallel)
  {
    ArgumentNullException.ThrowIfNull(gathering);
    ArgumentNullException.ThrowIfNull(farmProduction);
    _gathering = gathering;
    _farmProduction = farmProduction;
    _runSubcalculationsInParallel = runSubcalculationsInParallel;
  }

  public EconomyTickResult CalculateTick(EconomyTickInput input)
  {
    ArgumentNullException.ThrowIfNull(input);
    (GatheringOutcome wood, FarmProductionOutcome food) = SubcalculationRunner.Run(
        _runSubcalculationsInParallel,
        () => _gathering.Calculate(input.Tick, input.Gatherers, input.Trees),
        () => _farmProduction.Calculate(input.Tick, input.Farms));
    return Consolidate(input.Tick, wood, food);
  }

  /// <summary>
  /// Reglas 12 a 14: junta los resultados parciales en un orden fijo. Los eventos de recolección y árboles van primero y
  /// los de granjas después, sin importar qué subcálculo terminó antes.
  /// </summary>
  private static EconomyTickResult Consolidate(TickId tick, GatheringOutcome wood, FarmProductionOutcome food) => new(
      tick,
      SumDeltasByPlayer(wood.WoodGathered, food.FoodProduced),
      wood.WoodGathered,
      food.FoodProduced,
      wood.GathererUpdates,
      wood.WorkersToIdle,
      wood.TreeUpdates,
      food.FarmUpdates,
      wood.ProposedEvents.Concat(food.ProposedEvents).ToEquatableArray());

  /// <summary>Regla 12: suma madera y comida por jugador; solo deltas no nulos, ordenados por <c>PlayerId</c>.</summary>
  private static EquatableArray<PlayerResourceDelta> SumDeltasByPlayer(
      EquatableArray<WoodGathered> wood, EquatableArray<FoodProduced> food)
  {
    IEnumerable<(PlayerId Player, ResourceAmount Amount)> credits = wood
        .Select(w => (Player: w.Owner, Amount: ResourceAmount.OfWood(w.Amount)))
        .Concat(food.Select(f => (Player: f.Owner, Amount: ResourceAmount.OfFood(f.Amount))));
    return credits
        .GroupBy(c => c.Player)
        .Select(g => new PlayerResourceDelta(g.Key, g.Aggregate(ResourceAmount.Zero, (sum, c) => sum + c.Amount)))
        .Where(d => !d.Delta.IsZero)
        .OrderBy(d => d.Player)
        .ToEquatableArray();
  }

  private static EconomyConfig Validated(EconomyConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    return config;
  }
}
