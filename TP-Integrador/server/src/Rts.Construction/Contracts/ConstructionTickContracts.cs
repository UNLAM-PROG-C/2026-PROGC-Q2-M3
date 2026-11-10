using Rts.Construction.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Construction.Contracts;

/// <summary>
/// Entrada inmutable de la fase 3 (Construction), después de confirmar Movement (06 §3).
/// <list type="bullet">
/// <item><see cref="Buildings"/>: obras en construcción.</item>
/// <item><see cref="Builders"/>: trabajadores con orden Build en rango de su obra.</item>
/// <item><see cref="Repairs"/>: reparaciones de granja en curso (pagadas).</item>
/// <item><see cref="Repairers"/>: trabajadores con orden RepairFarm en rango de su granja.</item>
/// </list>
/// El orden de las colecciones no afecta el resultado. El constructor verifica IDs únicos, que cada aporte apunte a una
/// obra o reparación presente y que ningún trabajador aporte a dos cosas.
/// </summary>
public sealed record ConstructionTickInput
{
    public ConstructionTickInput(
        TickId tick,
        EquatableArray<BuildingUnderConstruction> buildings,
        EquatableArray<WorkerContribution> builders,
        EquatableArray<FarmRepairState> repairs,
        EquatableArray<WorkerContribution> repairers)
    {
        var buildingIds = UniqueSet(buildings.Select(b => b?.BuildingId ?? throw new ArgumentNullException(nameof(buildings))), nameof(buildings));
        var repairIds = UniqueSet(repairs.Select(r => r?.FarmId ?? throw new ArgumentNullException(nameof(repairs))), nameof(repairs));

        UniqueSet(
            builders.Concat(repairers).Select(w => w?.WorkerId ?? throw new ArgumentNullException(nameof(builders))),
            "builders/repairers");

        EnsureTargets(builders, buildingIds, nameof(builders));
        EnsureTargets(repairers, repairIds, nameof(repairers));

        Tick = tick;
        Buildings = buildings;
        Builders = builders;
        Repairs = repairs;
        Repairers = repairers;
    }

    public TickId Tick { get; }

    public EquatableArray<BuildingUnderConstruction> Buildings { get; }

    public EquatableArray<WorkerContribution> Builders { get; }

    public EquatableArray<FarmRepairState> Repairs { get; }

    public EquatableArray<WorkerContribution> Repairers { get; }

    private static HashSet<EntityId> UniqueSet(IEnumerable<EntityId> ids, string paramName)
    {
        var set = new HashSet<EntityId>();
        foreach (EntityId id in ids)
        {
            if (!set.Add(id))
            {
                throw new ArgumentException($"ID duplicado en la entrada: {id}.", paramName);
            }
        }

        return set;
    }

    private static void EnsureTargets(EquatableArray<WorkerContribution> workers, HashSet<EntityId> targets, string paramName)
    {
        foreach (WorkerContribution worker in workers)
        {
            if (!targets.Contains(worker.TargetId))
            {
                throw new ArgumentException($"El trabajador {worker.WorkerId} aporta a {worker.TargetId}, que no está en la entrada.", paramName);
            }
        }
    }
}

/// <summary>
/// Propuesta inmutable de la fase 3. Simulation la confirma antes de crear el snapshot de Economy ∥ Combat,
/// así Combat ve el progreso de este tick (02 §4). Colecciones por ID ascendente.
/// </summary>
/// <param name="Tick">Tick de la entrada.</param>
/// <param name="BuildingProgress">Nuevo HP de cada obra que recibió trabajo, y si se completó.</param>
/// <param name="RepairProgress">Nuevo trabajo acumulado de cada reparación que recibió trabajo, y si se completó.</param>
/// <param name="ProposedEvents">
/// <c>BuildingProgressChanged</c>/<c>BuildingCompleted</c> por BuildingId y luego
/// <c>FarmRepairProgressChanged</c>/<c>FarmRepairCompleted</c> por FarmId.
/// </param>
public sealed record ConstructionTickResult(
    TickId Tick,
    EquatableArray<BuildingProgress> BuildingProgress,
    EquatableArray<RepairProgress> RepairProgress,
    EquatableArray<IDomainEventPayload> ProposedEvents)
{
    public static ConstructionTickResult Empty(TickId tick) => new(tick, default, default, default);
}

/// <summary>Progreso de obra propuesto. Si <see cref="IsCompleted"/>, <see cref="Hp"/> es el HP máximo.</summary>
public sealed record BuildingProgress(EntityId BuildingId, int Hp, bool IsCompleted);

/// <summary>Progreso de reparación propuesto.</summary>
public sealed record RepairProgress(EntityId FarmId, int WorkDone, bool IsCompleted);
