using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
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

  /// <summary>
  /// Valida el objetivo de una orden <c>GatherWood</c> al procesar el comando (04 §3, RECOMENDADO D-092). Devuelve <c>null</c> si es
  /// recolectable, o el motivo del rechazo:
  /// <list type="bullet">
  /// <item><c>Common.EntityNotFound</c>: el objetivo no existe (<paramref name="target"/> nulo).</item>
  /// <item><c>Economy.InvalidGatherTarget</c>: el objetivo no es un árbol.</item>
  /// <item><c>Economy.TreeDepleted</c>: el árbol está agotado.</item>
  /// </list>
  /// La propiedad y el tipo del trabajador los validan Simulation y Movement. La alcanzabilidad la decide Movement.
  /// </summary>
  ErrorCode? EvaluateGatherTarget(GatherTargetInfo? target);
}
