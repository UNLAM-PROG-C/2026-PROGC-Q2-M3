using Rts.Production;
using Rts.Production.Model;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>TrainUnit</c> (05 §3): pide entrenar una unidad en un productor propio. No es un comando grupal: no lleva unidades ni rutas.
/// <list type="number">
/// <item>Simulation arma los datos del productor (<see cref="ProducerInfo"/>, o nulo si no existe ese edificio).</item>
/// <item>Production decide si se admite (<see cref="IProductionRules.EvaluateTrainRequest"/>): dueño, tipo, operativo, cola llena.
/// <b>No</b> se exige comida: se cobra al iniciar, en la fase 8.</item>
/// <item>Si se admite, Simulation agrega la solicitud al final de la cola, identificada por la <c>Sequence</c> del comando (D-041),
/// y emite <see cref="ProductionChanged"/>.</item>
/// </list>
/// Si Production admite algo que no es un productor propio, la respuesta es incoherente: falla crítica.
/// </summary>
internal sealed class TrainUnitCommandHandler : ICommandHandler
{
  private readonly IProductionRules _rules;

  public TrainUnitCommandHandler(IProductionRules rules)
  {
    ArgumentNullException.ThrowIfNull(rules);
    _rules = rules;
  }

  public CommandType Type => CommandType.TrainUnit;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var train = (TrainUnitCommand)command.Command;
    WorldState world = context.World;
    world.Buildings.TryGetValue(train.ProducerId, out BuildingEntity? building);

    ErrorCode? error = _rules.EvaluateTrainRequest(command.Player, train.UnitType, ProducerInfoOf(building, context.Tick));
    if (error is { } rejection)
    {
      return CommandResult.Rejected(command, context.Tick, rejection);
    }

    if (building?.Production is not { } producer)
    {
      throw new InvalidOperationException($"Production admitió un TrainUnit sobre {train.ProducerId}, que no es un productor completado.");
    }

    var request = new ProductionRequest(command.Sequence, producer.ProducerId, command.Player, train.UnitType);
    ProducerState queued = WithQueued(producer, request); // Su constructor verifica dueño y tipo de unidad.

    context.World = world.With(buildings: world.Buildings.SetItem(producer.ProducerId, building.WithProduction(queued)));
    context.Emit(ChangedEvent(queued));
    return CommandResult.Accepted(command, context.Tick);
  }

  /// <summary>
  /// Lo que Production necesita saber del edificio (05 §3). Operativo: completado y con <c>OperationalFromTick</c> alcanzado
  /// (un Cuartel terminado en N opera desde N+1). La cola ya incluye lo encolado antes en el mismo lote.
  /// </summary>
  private static ProducerInfo? ProducerInfoOf(BuildingEntity? building, TickId tick) => building is null
      ? null
      : new ProducerInfo(
          building.Id,
          building.Owner,
          building.Type,
          IsOperational: building.Production?.IsOperationalAt(tick) ?? false,
          PendingCount: building.Production?.Pending.Count ?? 0);

  /// <summary>Copia del productor con la solicitud al final de su cola FIFO. La tarea activa no cambia.</summary>
  private static ProducerState WithQueued(ProducerState producer, ProductionRequest request) => new(
      producer.ProducerId,
      producer.Owner,
      producer.ProducerType,
      producer.Footprint,
      producer.OperationalFromTick,
      producer.ActiveJob,
      [.. producer.Pending, request]);

  /// <summary>Estado discreto del productor para su dueño (D-042): estado, tarea activa y tipos pendientes.</summary>
  private static ProductionChanged ChangedEvent(ProducerState producer) => new(
      producer.ProducerId,
      producer.Owner,
      producer.Status,
      producer.ActiveJob?.Request.UnitType,
      producer.ActiveJob?.RemainingTicks,
      [.. producer.Pending.Select(request => request.UnitType)]);
}
