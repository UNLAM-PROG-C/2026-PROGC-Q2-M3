using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production.Model;

/// <summary>
/// Vista de producción inmutable de un edificio productor completado: entrada del tick y estado propuesto en el resultado.
/// Una sola tarea activa como máximo y una cola FIFO de pendientes, ordenada por Sequence de admisión (05 §2).
/// <see cref="Status"/> se deriva del contenido.
/// </summary>
public sealed record ProducerState
{
    public ProducerState(
        EntityId producerId,
        PlayerId owner,
        BuildingType producerType,
        Footprint footprint,
        TickId operationalFromTick,
        ProductionJob? activeJob,
        EquatableArray<ProductionRequest> pending)
    {
        if (producerId.IsNone)
        {
            throw new ArgumentException("El productor debe tener un EntityId válido.", nameof(producerId));
        }

        if (!owner.IsValid)
        {
            throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
        }

        UnitType producedUnit = ProducerCatalog.GetProducedUnit(producerType)
            ?? throw new ArgumentException($"{producerType} no es un edificio productor.", nameof(producerType));

        if (activeJob is not null)
        {
            EnsureBelongs(activeJob.Request, producerId, owner, producedUnit, nameof(activeJob));
        }

        CommandSequence? previous = null;
        foreach (ProductionRequest request in pending)
        {
            ArgumentNullException.ThrowIfNull(request, nameof(pending));
            EnsureBelongs(request, producerId, owner, producedUnit, nameof(pending));
            if (previous is { } prev && request.Sequence <= prev)
            {
                throw new ArgumentException("La cola pendiente debe estar en orden FIFO (Sequence creciente).", nameof(pending));
            }

            previous = request.Sequence;
        }

        ProducerId = producerId;
        Owner = owner;
        ProducerType = producerType;
        Footprint = footprint;
        OperationalFromTick = operationalFromTick;
        ActiveJob = activeJob;
        Pending = pending;
    }

    public EntityId ProducerId { get; }

    public PlayerId Owner { get; }

    public BuildingType ProducerType { get; }

    /// <summary>Huella del productor; base de la búsqueda de celda de aparición.</summary>
    public Footprint Footprint { get; }

    /// <summary>Primer tick operativo (N+1 si se completó en N; tick inicial para el Centro Principal).</summary>
    public TickId OperationalFromTick { get; }

    public ProductionJob? ActiveJob { get; }

    public EquatableArray<ProductionRequest> Pending { get; }

    public ProducerStatus Status => ActiveJob switch
    {
        { IsPendingSpawn: true } => ProducerStatus.PendingSpawn,
        not null => ProducerStatus.Training,
        null when !Pending.IsEmpty => ProducerStatus.WaitingForResources,
        _ => ProducerStatus.Idle,
    };

    public bool IsOperationalAt(TickId tick) => tick >= OperationalFromTick;

    private static void EnsureBelongs(ProductionRequest request, EntityId producerId, PlayerId owner, UnitType producedUnit, string paramName)
    {
        if (request.ProducerId != producerId || request.Owner != owner || request.UnitType != producedUnit)
        {
            throw new ArgumentException(
                $"La solicitud {request.Sequence} no corresponde al productor {producerId} ({owner}, {producedUnit}).", paramName);
        }
    }
}
