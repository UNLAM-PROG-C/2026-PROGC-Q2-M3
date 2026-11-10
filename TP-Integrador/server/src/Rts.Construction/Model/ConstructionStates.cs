using Rts.Shared.Entities;
using Rts.Shared.Ids;

namespace Rts.Construction.Model;

/// <summary>
/// Edificio en obra en el snapshot de la fase 3. Su HP representa también el progreso (03 §3).
/// El HP máximo sale de <c>ConstructionConfig</c> según <see cref="BuildingType"/> (D-021).
/// </summary>
public sealed record BuildingUnderConstruction
{
    public BuildingUnderConstruction(EntityId buildingId, PlayerId owner, BuildingType buildingType, int hp)
    {
        if (buildingId.IsNone)
        {
            throw new ArgumentException("El edificio debe tener un EntityId válido.", nameof(buildingId));
        }

        if (!owner.IsValid)
        {
            throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
        }

        if (!Enum.IsDefined(buildingType) || buildingType == BuildingType.MainCenter)
        {
            throw new ArgumentException($"{buildingType} no puede estar en obra.", nameof(buildingType));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hp);

        BuildingId = buildingId;
        Owner = owner;
        BuildingType = buildingType;
        Hp = hp;
    }

    public EntityId BuildingId { get; }

    public PlayerId Owner { get; }

    public BuildingType BuildingType { get; }

    public int Hp { get; }
}

/// <summary>
/// Reparación de capacidad en curso de una granja agotada (pagada). DECIDIDO (D-012): el progreso es de Construction.
/// Existe solo mientras dura la reparación; el progreso se conserva aunque no haya trabajadores (03 §4).
/// </summary>
public sealed record FarmRepairState
{
    public FarmRepairState(EntityId farmId, PlayerId owner, int workDone)
    {
        if (farmId.IsNone)
        {
            throw new ArgumentException("La granja debe tener un EntityId válido.", nameof(farmId));
        }

        if (!owner.IsValid)
        {
            throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(workDone);

        FarmId = farmId;
        Owner = owner;
        WorkDone = workDone;
    }

    public EntityId FarmId { get; }

    public PlayerId Owner { get; }

    /// <summary>Trabajo acumulado; la reparación termina al alcanzar <c>RepairWorkRequired</c>.</summary>
    public int WorkDone { get; }
}

/// <summary>
/// Trabajador que aporta en este tick a una obra o a una reparación: tiene la orden correspondiente sobre
/// <see cref="TargetId"/> y está en rango de interacción según Movement. Simulation hace el filtrado (como en Economy, D-016).
/// </summary>
public sealed record WorkerContribution
{
    public WorkerContribution(EntityId workerId, EntityId targetId)
    {
        if (workerId.IsNone || targetId.IsNone)
        {
            throw new ArgumentException("Trabajador y objetivo deben tener EntityId válidos.");
        }

        WorkerId = workerId;
        TargetId = targetId;
    }

    public EntityId WorkerId { get; }

    public EntityId TargetId { get; }
}
