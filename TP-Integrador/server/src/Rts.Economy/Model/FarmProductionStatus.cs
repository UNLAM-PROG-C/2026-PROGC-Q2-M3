namespace Rts.Economy.Model;

/// <summary>
/// Estado productivo de una granja completada (01 §4). La reparación de capacidad y su progreso
/// pertenecen a Construction (D-012); para Economy, una granja en reparación sigue <see cref="Depleted"/>.
/// </summary>
public enum FarmProductionStatus
{
    /// <summary>Tiene tandas disponibles.</summary>
    Producing = 1,

    /// <summary>Consumió sus tandas: no produce, pero no está destruida (sigue en el mapa y conserva HP).</summary>
    Depleted = 2,
}
