namespace Rts.Shared.Entities;

/// <summary>
/// Tipo de la orden activa de una unidad (04 §2). Cada unidad tiene una única orden activa.
/// <c>Stop</c> no es un tipo de orden: cancela la actual y deja la unidad en <see cref="Idle"/>.
/// </summary>
public enum UnitOrderKind
{
  Idle = 1,
  Move = 2,
  GatherWood = 3,
  Build = 4,
  RepairFarm = 5,
  Attack = 6,
}
