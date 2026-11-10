using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production;

/// <summary>Reglas de producción que Simulation consulta fuera del cálculo del tick. Implementaciones puras y seguras para uso concurrente.</summary>
public interface IProductionRules
{
    /// <summary>Duración de entrenamiento del tipo de unidad, en ticks.</summary>
    int GetTrainingDurationTicks(UnitType unitType);

    /// <summary>
    /// Valida un <c>TrainUnit</c> en la fase 1 (05 §7, <c>EvaluateTrainRequest</c>). Devuelve <c>null</c> si se admite:
    /// <c>Common.EntityNotFound</c> (productor nulo), <c>Common.NotOwner</c>, <c>Production.NotAProducer</c>,
    /// <c>Production.WrongUnitType</c>, <c>Production.ProducerNotOperational</c>, <c>Production.QueueFull</c>.
    /// No exige comida: se cobra al iniciar.
    /// </summary>
    ErrorCode? EvaluateTrainRequest(PlayerId requester, UnitType unitType, ProducerInfo? producer);

    /// <summary>
    /// Primera celda apta alrededor de la huella, en el orden fijo del contrato 05 §5 (D-039), salteando
    /// <paramref name="alreadyReserved"/>. Devuelve <c>null</c> si no hay ninguna dentro del radio.
    /// Simulation también puede usarla para ubicar las unidades iniciales.
    /// </summary>
    GridCell? FindSpawnCell(Footprint producerFootprint, SpawnView view, IReadOnlySet<GridCell> alreadyReserved);
}
