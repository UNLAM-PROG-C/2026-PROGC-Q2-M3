using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Economy.Calculations;

/// <summary>
/// Colecciones de trabajo de una sola llamada a <see cref="GatheringCalculator.Calculate"/>. Es local a esa llamada (nunca
/// se comparte entre hilos), así que el cálculo sigue siendo una función pura aunque estas listas sean mutables.
/// </summary>
internal sealed class GatheringAccumulator
{
  public List<WoodGathered> WoodGathered { get; } = [];

  public List<GathererState> GathererUpdates { get; } = [];

  public List<EntityId> WorkersToIdle { get; } = [];

  public List<TreeEconomyState> TreeUpdates { get; } = [];

  /// <summary>Se agregan recorriendo los árboles por <c>TreeId</c>, así que ya quedan en el orden del contrato (01 §4.14).</summary>
  public List<IDomainEventPayload> Events { get; } = [];

  /// <summary>Arma el resultado con cada colección ordenada por ID ascendente (01 §4.13).</summary>
  public GatheringOutcome ToOutcome() => new(
      WoodGathered.OrderBy(w => w.WorkerId).ToEquatableArray(),
      GathererUpdates.OrderBy(g => g.WorkerId).ToEquatableArray(),
      WorkersToIdle.Order().ToEquatableArray(),
      TreeUpdates.OrderBy(t => t.TreeId).ToEquatableArray(),
      Events.ToEquatableArray());
}
