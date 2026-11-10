namespace Rts.Shared.Entities;

/// <summary>Tipos de unidad (04 §2). El valor 0 no está definido a propósito, para detectar valores sin inicializar.</summary>
public enum UnitType
{
    /// <summary>Trabajador: Move, GatherWood, Build, RepairFarm, Stop. No ataca.</summary>
    Worker = 1,

    /// <summary>Soldado cuerpo a cuerpo: Move, Attack, Stop.</summary>
    Soldier = 2,
}
