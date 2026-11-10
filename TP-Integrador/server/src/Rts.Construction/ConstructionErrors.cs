using Rts.Shared.Errors;

namespace Rts.Construction;

/// <summary>
/// Códigos de error del área Construction (D-007). Para objetivos inexistentes o ajenos se usan
/// <c>Common.EntityNotFound</c> y <c>Common.NotOwner</c>; la falta de madera la informa Economy.
/// </summary>
public static class ConstructionErrors
{
    /// <summary>El tipo no se construye durante la partida (Centro Principal).</summary>
    public static readonly ErrorCode NotBuildable = new("Construction.NotBuildable");

    /// <summary>La huella sale de los límites del mapa.</summary>
    public static readonly ErrorCode OutOfBounds = new("Construction.OutOfBounds");

    /// <summary>La huella se superpone con otro edificio o valla, en obra o completado.</summary>
    public static readonly ErrorCode CellOccupied = new("Construction.CellOccupied");

    /// <summary>La huella cubre celdas reservadas de un árbol, visible o agotado.</summary>
    public static readonly ErrorCode TreeReserved = new("Construction.TreeReserved");

    /// <summary>Hay una unidad presente en la huella al momento de colocar.</summary>
    public static readonly ErrorCode UnitInFootprint = new("Construction.UnitInFootprint");

    /// <summary>Ningún trabajador del comando puede alcanzar una posición de construcción tras la colocación hipotética.</summary>
    public static readonly ErrorCode NoReachableWorker = new("Construction.NoReachableWorker");

    /// <summary>"Continuar" sobre un edificio que ya está completado (ya no es una obra).</summary>
    public static readonly ErrorCode AlreadyCompleted = new("Construction.AlreadyCompleted");

    /// <summary>RepairFarm sobre algo que no es una granja completada.</summary>
    public static readonly ErrorCode NotAFarm = new("Construction.NotAFarm");

    /// <summary>RepairFarm sobre una granja que todavía tiene tandas (03 §4).</summary>
    public static readonly ErrorCode FarmNotDepleted = new("Construction.FarmNotDepleted");
}
