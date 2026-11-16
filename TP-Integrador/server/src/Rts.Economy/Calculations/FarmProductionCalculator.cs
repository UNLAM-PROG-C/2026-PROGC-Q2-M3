using Rts.Economy.Configuration;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Economy.Calculations;

/// <summary>
/// Producción de granjas (contrato 01 §4, reglas 8 a 11). Lee una entrada inmutable y devuelve una propuesta; no comparte
/// estado con <see cref="GatheringCalculator"/>, por eso pueden correr en paralelo.
/// </summary>
internal sealed class FarmProductionCalculator : IFarmProductionCalculation
{
  private const int CounterStep = 1;
  private const int CounterReset = 0;
  private const int BatchesPerProduction = 1;
  private const int NoBatches = 0;

  private readonly int _productionIntervalTicks;
  private readonly int _foodPerBatch;

  public FarmProductionCalculator(EconomyConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    _productionIntervalTicks = config.FarmProductionIntervalTicks;
    _foodPerBatch = config.FoodPerFarmBatch;
  }

  public FarmProductionOutcome Calculate(TickId tick, EquatableArray<FarmEconomyState> farms)
  {
    var accumulator = new FarmProductionAccumulator();
    foreach (FarmEconomyState farm in farms.Where(f => IsAdvancing(f, tick)).OrderBy(f => f.FarmId))
    {
      AdvanceFarm(farm, accumulator);
    }

    return accumulator.ToOutcome();
  }

  /// <summary>
  /// Regla 8: el temporizador avanza solo desde <c>ProductionActiveFromTick</c> (N+1 si se completó o reparó en N, D-014).
  /// Regla 11: una granja <c>Depleted</c> no cambia en Economy.
  /// </summary>
  private static bool IsAdvancing(FarmEconomyState farm, TickId tick) =>
      farm.Status == FarmProductionStatus.Producing && tick >= farm.ProductionActiveFromTick;

  /// <summary>Regla 9: al completar el intervalo produce una tanda para su dueño; si no, solo suma un tick.</summary>
  private void AdvanceFarm(FarmEconomyState farm, FarmProductionAccumulator accumulator)
  {
    int ticksAccumulated = farm.TicksAccumulated + CounterStep;
    if (ticksAccumulated < _productionIntervalTicks)
    {
      accumulator.FarmUpdates.Add(WithTimer(farm, ticksAccumulated));
      return;
    }

    accumulator.FoodProduced.Add(new FoodProduced(farm.FarmId, farm.Owner, _foodPerBatch));
    FarmEconomyState afterBatch = ConsumeBatch(farm);
    accumulator.FarmUpdates.Add(afterBatch);
    ProposeDepletionIfNeeded(afterBatch, accumulator);
  }

  /// <summary>
  /// Descuenta una tanda y reinicia el temporizador. Regla 10: con 0 tandas pasa a <c>Depleted</c>; no se destruye ni
  /// pierde HP (eso no es de Economy).
  /// </summary>
  private static FarmEconomyState ConsumeBatch(FarmEconomyState farm)
  {
    int batchesRemaining = farm.BatchesRemaining - BatchesPerProduction;
    FarmProductionStatus status = batchesRemaining == NoBatches
        ? FarmProductionStatus.Depleted
        : FarmProductionStatus.Producing;
    return new FarmEconomyState(farm.FarmId, farm.Owner, status, batchesRemaining, CounterReset, farm.ProductionActiveFromTick);
  }

  private static void ProposeDepletionIfNeeded(FarmEconomyState farm, FarmProductionAccumulator accumulator)
  {
    if (farm.Status == FarmProductionStatus.Depleted)
    {
      accumulator.Events.Add(new FarmDepleted(farm.FarmId));
    }
  }

  private static FarmEconomyState WithTimer(FarmEconomyState farm, int ticksAccumulated) =>
      new(farm.FarmId, farm.Owner, farm.Status, farm.BatchesRemaining, ticksAccumulated, farm.ProductionActiveFromTick);
}
