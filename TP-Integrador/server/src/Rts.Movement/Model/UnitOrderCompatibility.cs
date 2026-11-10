using Rts.Shared.Entities;

namespace Rts.Movement.Model;

/// <summary>
/// Tabla de órdenes permitidas por tipo de unidad. DECIDIDO (04 §2):
/// Trabajador: Move, GatherWood, Build, RepairFarm, Stop. Soldado: Move, Attack, Stop.
/// <c>Idle</c> (resultado de Stop) está permitido para todos.
/// </summary>
public static class UnitOrderCompatibility
{
  public static bool IsAllowed(UnitType unitType, UnitOrderKind orderKind) => (unitType, orderKind) switch
  {
    (_, UnitOrderKind.Idle) => true,
    (_, UnitOrderKind.Move) => true,
    (UnitType.Worker, UnitOrderKind.GatherWood or UnitOrderKind.Build or UnitOrderKind.RepairFarm) => true,
    (UnitType.Soldier, UnitOrderKind.Attack) => true,
    _ => false,
  };

  /// <summary>Órdenes que apuntan a una entidad y se mantienen activas al llegar al rango (04 §2).</summary>
  public static bool IsTaskOrder(UnitOrderKind orderKind) =>
      orderKind is UnitOrderKind.GatherWood or UnitOrderKind.Build or UnitOrderKind.RepairFarm or UnitOrderKind.Attack;
}
