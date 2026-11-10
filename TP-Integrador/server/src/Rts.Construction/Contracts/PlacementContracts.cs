using Rts.Construction.Model;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Construction.Contracts;

/// <summary>
/// Resultado de validar el sitio de una colocación (03 §2), sin considerar trabajadores ni costo.
/// Si es válido, trae la huella resultante, que Simulation usa para armar el mapa hipotético de Movement.
/// </summary>
public sealed record SiteEvaluation
{
    private SiteEvaluation(Footprint? footprint, ErrorCode? error)
    {
        Footprint = footprint;
        Error = error;
    }

    public bool IsValid => Error is null;

    public Footprint? Footprint { get; }

    public ErrorCode? Error { get; }

    public static SiteEvaluation Valid(Footprint footprint) => new(footprint, null);

    public static SiteEvaluation Invalid(ErrorCode error)
    {
        if (error.IsDefault)
        {
            throw new ArgumentException("Un sitio inválido requiere código de error.", nameof(error));
        }

        return new SiteEvaluation(null, error);
    }
}

/// <summary>
/// Resultado de Movement para un trabajador del Build, evaluado contra el mapa con la huella hipotética (03 §5, D-026).
/// <see cref="MovementError"/> nulo significa que su ruta es válida y termina en rango de la huella.
/// </summary>
public sealed record WorkerPlacementCandidate(EntityId WorkerId, ErrorCode? MovementError);

/// <summary>Entrada de <c>EvaluatePlacement</c>: sitio y resultado de Movement para cada trabajador del comando.</summary>
public sealed record PlacementInput
{
    public PlacementInput(
        PlayerId owner,
        BuildingType buildingType,
        GridCell anchor,
        OccupancyView site,
        EquatableArray<WorkerPlacementCandidate> workers)
    {
        if (!owner.IsValid)
        {
            throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
        }

        ArgumentNullException.ThrowIfNull(site);
        if (workers.IsEmpty)
        {
            throw new ArgumentException("Un Build requiere al menos un trabajador.", nameof(workers));
        }

        if (workers.Select(w => w?.WorkerId ?? throw new ArgumentNullException(nameof(workers))).Distinct().Count() != workers.Count)
        {
            throw new ArgumentException("Trabajador repetido en el Build.", nameof(workers));
        }

        Owner = owner;
        BuildingType = buildingType;
        Anchor = anchor;
        Site = site;
        Workers = workers;
    }

    public PlayerId Owner { get; }

    public BuildingType BuildingType { get; }

    public GridCell Anchor { get; }

    public OccupancyView Site { get; }

    public EquatableArray<WorkerPlacementCandidate> Workers { get; }
}

/// <summary>
/// Propuesta de colocación (03 §6, <c>EvaluatePlacement</c>). Si se acepta, Simulation todavía debe confirmar el costo con
/// Economy y aplicar todo en un único commit atómico: gasto + obra + ocupación + órdenes de los trabajadores aceptados.
/// <see cref="WorkerResults"/> informa cada trabajador (aceptado o rechazado con su motivo) en ambos casos.
/// </summary>
public sealed record PlacementEvaluation
{
    private PlacementEvaluation(
        ErrorCode? error,
        Footprint? footprint,
        int initialHp,
        EquatableArray<EntityCommandResult> workerResults)
    {
        Error = error;
        Footprint = footprint;
        InitialHp = initialHp;
        WorkerResults = workerResults;
    }

    public bool IsAccepted => Error is null;

    public ErrorCode? Error { get; }

    public Footprint? Footprint { get; }

    /// <summary>HP con el que nace la obra; 0 si se rechazó.</summary>
    public int InitialHp { get; }

    public EquatableArray<EntityCommandResult> WorkerResults { get; }

    public IEnumerable<EntityId> AcceptedWorkers => WorkerResults.Where(r => r.IsAccepted).Select(r => r.Entity);

    public static PlacementEvaluation Accepted(Footprint footprint, int initialHp, EquatableArray<EntityCommandResult> workerResults)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialHp);
        if (!workerResults.Any(r => r.IsAccepted))
        {
            throw new ArgumentException("Una colocación aceptada requiere al menos un trabajador aceptado (03 §8).", nameof(workerResults));
        }

        return new PlacementEvaluation(null, footprint, initialHp, workerResults);
    }

    public static PlacementEvaluation Rejected(ErrorCode error, EquatableArray<EntityCommandResult> workerResults = default)
    {
        if (error.IsDefault)
        {
            throw new ArgumentException("El rechazo requiere código de error.", nameof(error));
        }

        if (workerResults.Any(r => r.IsAccepted))
        {
            throw new ArgumentException("Una colocación rechazada no acepta trabajadores.", nameof(workerResults));
        }

        return new PlacementEvaluation(error, null, 0, workerResults);
    }
}

/// <summary>Cómo procede un <c>RepairFarm</c> válido (03 §4).</summary>
public enum RepairStartKind
{
    /// <summary>La granja está agotada sin reparación: se paga la madera una vez y comienza la reparación.</summary>
    StartAndPay = 1,

    /// <summary>Ya hay una reparación en curso: los trabajadores se suman sin pagar de nuevo.</summary>
    Join = 2,
}

/// <summary>Resultado de <c>EvaluateRepairStart</c>.</summary>
public sealed record RepairStartEvaluation
{
    private RepairStartEvaluation(RepairStartKind? kind, ErrorCode? error)
    {
        Kind = kind;
        Error = error;
    }

    public bool IsValid => Error is null;

    public RepairStartKind? Kind { get; }

    public ErrorCode? Error { get; }

    public static RepairStartEvaluation Valid(RepairStartKind kind) => new(kind, null);

    public static RepairStartEvaluation Invalid(ErrorCode error)
    {
        if (error.IsDefault)
        {
            throw new ArgumentException("El rechazo requiere código de error.", nameof(error));
        }

        return new RepairStartEvaluation(null, error);
    }
}
