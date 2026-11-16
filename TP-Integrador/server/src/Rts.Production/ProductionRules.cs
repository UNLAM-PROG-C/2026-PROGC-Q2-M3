using Rts.Production.Calculation;
using Rts.Production.Configuration;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production;

/// <summary>
/// Implementación de <see cref="IProductionRules"/> (contrato 05): las consultas que Simulation hace fuera del cálculo del tick.
/// Sin estado mutable: segura para usarse desde cualquier hilo.
/// </summary>
public sealed class ProductionRules : IProductionRules
{
  private readonly ProductionConfig _config;

  /// <exception cref="ArgumentException">Si la configuración no es válida.</exception>
  public ProductionRules(ProductionConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    _config = config;
  }

  /// <inheritdoc/>
  public int GetTrainingDurationTicks(UnitType unitType) =>
      _config.TrainingDurationTicks.TryGetValue(unitType, out int ticks)
          ? ticks
          : throw new ArgumentOutOfRangeException(nameof(unitType), unitType, "Tipo de unidad sin duración de entrenamiento.");

  /// <inheritdoc/>
  /// <remarks>Las verificaciones van en el orden del contrato 05 §3: el primer error que se encuentra es el que se informa.</remarks>
  public ErrorCode? EvaluateTrainRequest(PlayerId requester, UnitType unitType, ProducerInfo? producer)
  {
    if (producer is null)
    {
      return CommonErrors.EntityNotFound;
    }

    return producer.Owner != requester ? CommonErrors.NotOwner : EvaluateProducer(unitType, producer);
  }

  /// <inheritdoc/>
  public GridCell? FindSpawnCell(Footprint producerFootprint, SpawnView view, IReadOnlySet<GridCell> alreadyReserved)
  {
    ArgumentNullException.ThrowIfNull(view);
    ArgumentNullException.ThrowIfNull(alreadyReserved);
    return SpawnCellFinder.Find(producerFootprint, view, alreadyReserved, _config.SpawnSearchRadiusCells);
  }

  /// <summary>Lo que se verifica del edificio una vez que existe y es del jugador. No se exige comida: se cobra al iniciar (05 §2).</summary>
  private ErrorCode? EvaluateProducer(UnitType unitType, ProducerInfo producer)
  {
    UnitType? produced = ProducerCatalog.GetProducedUnit(producer.BuildingType);
    if (produced is null)
    {
      return ProductionErrors.NotAProducer;
    }

    return produced != unitType ? ProductionErrors.WrongUnitType : EvaluateAvailability(producer);
  }

  /// <summary>Un productor válido para ese tipo de unidad: tiene que estar operativo y con lugar en la cola.</summary>
  private ErrorCode? EvaluateAvailability(ProducerInfo producer)
  {
    if (!producer.IsOperational)
    {
      return ProductionErrors.ProducerNotOperational;
    }

    return producer.PendingCount < _config.MaxPendingPerProducer ? null : ProductionErrors.QueueFull;
  }
}
