using Rts.Production;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Fake determinista de las reglas de Production (el módulo real es de Joaco). <see cref="EvaluateTrainRequest"/> admite todo,
/// salvo que se cargue <see cref="Rejection"/>. Guarda los pedidos para revisar qué le pasó Simulation. Los demás métodos
/// todavía no los usa ninguna prueba.
/// </summary>
internal sealed class FakeProductionRules : IProductionRules
{
  private readonly List<TrainRequestCall> _trainRequests = [];

  /// <summary>Error que devuelve el fake para todo <c>TrainUnit</c>; nulo ⇒ admite.</summary>
  public ErrorCode? Rejection { get; set; }

  public IReadOnlyList<TrainRequestCall> TrainRequests => _trainRequests;

  public ErrorCode? EvaluateTrainRequest(PlayerId requester, UnitType unitType, ProducerInfo? producer)
  {
    _trainRequests.Add(new TrainRequestCall(requester, unitType, producer));
    return Rejection;
  }

  public int GetTrainingDurationTicks(UnitType unitType) => throw NotYet();

  public GridCell? FindSpawnCell(Footprint producerFootprint, SpawnView view, IReadOnlySet<GridCell> alreadyReserved) => throw NotYet();

  private static NotSupportedException NotYet() => new("Todavía no lo usa ninguna prueba de Simulation.");
}

/// <summary>Un pedido que Simulation le hizo a <see cref="FakeProductionRules.EvaluateTrainRequest"/>.</summary>
internal sealed record TrainRequestCall(PlayerId Requester, UnitType UnitType, ProducerInfo? Producer);
