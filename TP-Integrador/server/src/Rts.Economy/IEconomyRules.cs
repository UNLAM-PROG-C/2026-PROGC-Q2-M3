using Rts.Economy.Contracts;
using Rts.Shared.Entities;
using Rts.Shared.Resources;

namespace Rts.Economy;

/// <summary>
/// Reglas económicas que Simulation consulta para coordinar gastos atómicos (01 §5-6: <c>GetCost</c>,
/// <c>CanAfford</c>/<c>EvaluateSpend</c>). Economy es la fuente única de costos (D-015): Construction y Production
/// no leen costos. Ningún método muta saldos; el descuento lo confirma Simulation junto con la operación.
/// Implementaciones puras y seguras para uso concurrente.
/// </summary>
public interface IEconomyRules
{
  /// <summary>Saldo inicial de cada jugador, igual para ambos (06 §2).</summary>
  ResourceAmount InitialBalance { get; }

  /// <summary>
  /// Costo en madera de colocar un edificio. Devuelve <c>false</c> para tipos que no se construyen durante la partida
  /// (<see cref="BuildingType.MainCenter"/>).
  /// </summary>
  bool TryGetBuildingCost(BuildingType buildingType, out ResourceAmount cost);

  /// <summary>Costo en madera de iniciar la reparación de capacidad de una granja agotada (se paga una sola vez).</summary>
  ResourceAmount GetFarmRepairCost();

  /// <summary>Costo en comida de iniciar el entrenamiento de una unidad (se paga al iniciar, no al encolar).</summary>
  ResourceAmount GetTrainingCost(UnitType unitType);

  /// <summary>Evalúa si <paramref name="balance"/> cubre <paramref name="cost"/> y el saldo resultante.</summary>
  /// <exception cref="ArgumentException">Si el costo tiene componentes negativas.</exception>
  SpendEvaluation EvaluateSpend(ResourceAmount balance, ResourceAmount cost);

  /// <summary>Atajo de <see cref="EvaluateSpend"/>: verdadero si el saldo cubre el costo.</summary>
  bool CanAfford(ResourceAmount balance, ResourceAmount cost);
}
