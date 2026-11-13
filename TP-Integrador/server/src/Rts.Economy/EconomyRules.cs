using System.Collections.Immutable;
using Rts.Economy.Configuration;
using Rts.Economy.Contracts;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Resources;

namespace Rts.Economy;

/// <summary>
/// Reglas económicas que Simulation consulta para coordinar gastos atómicos (contrato 01 §1 y §5). Economy es la fuente
/// única de costos y del saldo inicial (D-015). Ningún método muta saldos: Simulation confirma el descuento en el mismo
/// commit que la operación. La instancia es inmutable, así que es segura para uso concurrente.
/// </summary>
public sealed class EconomyRules : IEconomyRules
{
  private readonly ImmutableDictionary<BuildingType, int> _buildingWoodCosts;
  private readonly ImmutableDictionary<UnitType, int> _unitFoodCosts;
  private readonly int _farmRepairWoodCost;

  public EconomyRules(EconomyConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    InitialBalance = config.InitialBalance;
    _farmRepairWoodCost = config.FarmRepairWoodCost;

    // Copias inmutables: las tablas de la configuración podrían modificarse desde afuera después de construir esto.
    _buildingWoodCosts = config.BuildingWoodCosts.ToImmutableDictionary();
    _unitFoodCosts = config.UnitFoodCosts.ToImmutableDictionary();
  }

  public ResourceAmount InitialBalance { get; }

  /// <summary>El Centro Principal no tiene costo porque no se construye durante la partida: devuelve <c>false</c>.</summary>
  public bool TryGetBuildingCost(BuildingType buildingType, out ResourceAmount cost)
  {
    bool found = _buildingWoodCosts.TryGetValue(buildingType, out int wood);
    cost = found ? ResourceAmount.OfWood(wood) : ResourceAmount.Zero;
    return found;
  }

  public ResourceAmount GetFarmRepairCost() => ResourceAmount.OfWood(_farmRepairWoodCost);

  /// <exception cref="ArgumentOutOfRangeException">Si el tipo de unidad no tiene costo configurado.</exception>
  public ResourceAmount GetTrainingCost(UnitType unitType)
  {
    if (!_unitFoodCosts.TryGetValue(unitType, out int food))
    {
      throw new ArgumentOutOfRangeException(nameof(unitType), unitType, "Tipo de unidad sin costo de entrenamiento.");
    }

    return ResourceAmount.OfFood(food);
  }

  /// <summary>
  /// Evalúa el gasto sin mutar nada. Si no alcanza, el saldo resultante es el original y el error dice qué falta.
  /// </summary>
  public SpendEvaluation EvaluateSpend(ResourceAmount balance, ResourceAmount cost)
  {
    if (!cost.IsNonNegative)
    {
      throw new ArgumentException($"El costo no puede tener componentes negativas ({cost}).", nameof(cost));
    }

    return FindShortage(balance, cost) is { } error
        ? SpendEvaluation.Insufficient(balance, error)
        : SpendEvaluation.Affordable(balance - cost);
  }

  public bool CanAfford(ResourceAmount balance, ResourceAmount cost) => EvaluateSpend(balance, cost).IsAffordable;

  /// <summary>Devuelve qué recurso falta, o <c>null</c> si el saldo cubre el costo.</summary>
  private static ErrorCode? FindShortage(ResourceAmount balance, ResourceAmount cost)
  {
    bool lacksWood = balance.Wood < cost.Wood;
    bool lacksFood = balance.Food < cost.Food;
    return (lacksWood, lacksFood) switch
    {
      (true, true) => EconomyErrors.InsufficientResources,
      (true, false) => EconomyErrors.InsufficientWood,
      (false, true) => EconomyErrors.InsufficientFood,
      _ => null,
    };
  }
}
