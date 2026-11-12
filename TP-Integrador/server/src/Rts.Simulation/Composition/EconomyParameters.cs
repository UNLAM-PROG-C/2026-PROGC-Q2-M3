using Rts.Economy.Configuration;

namespace Rts.Simulation.Composition;

/// <summary>
/// Los valores de <see cref="EconomyConfig"/> que Simulation necesita para crear estado y que <c>IEconomyRules</c> no expone
/// (D-089): la madera inicial de cada árbol y las tandas de una granja recién completada o reparada (01 §5, D-014). Se toman de la
/// configuración de Economy para no duplicarlos.
/// </summary>
public sealed record EconomyParameters
{
  public EconomyParameters(int maxWoodPerTree, int batchesPerFarmCycle)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWoodPerTree);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchesPerFarmCycle);
    MaxWoodPerTree = maxWoodPerTree;
    BatchesPerFarmCycle = batchesPerFarmCycle;
  }

  public int MaxWoodPerTree { get; }

  public int BatchesPerFarmCycle { get; }

  public static EconomyParameters FromConfig(EconomyConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    return new EconomyParameters(config.MaxWoodPerTree, config.BatchesPerFarmCycle);
  }
}
