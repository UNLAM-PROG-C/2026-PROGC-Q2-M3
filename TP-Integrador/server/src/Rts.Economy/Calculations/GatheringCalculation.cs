using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Economy.Calculations;

/// <summary>
/// Subcálculo interno de recolección y ciclo de vida de árboles (01 §6-7: <c>GatheringCalculation</c>).
/// Puede ejecutarse en paralelo con <see cref="IFarmProductionCalculation"/>; no comparte estado mutable con él.
/// Incluye: avance de contadores, reparto determinista por WorkerId, agotamiento y regeneración.
/// </summary>
internal interface IGatheringCalculation
{
    GatheringOutcome Calculate(
        TickId tick,
        EquatableArray<GathererState> gatherers,
        EquatableArray<TreeEconomyState> trees);
}

/// <summary>Resultado parcial de <see cref="IGatheringCalculation"/>; se consolida en <c>EconomyTickResult</c> en orden fijo.</summary>
internal sealed record GatheringOutcome(
    EquatableArray<Contracts.WoodGathered> WoodGathered,
    EquatableArray<GathererState> GathererUpdates,
    EquatableArray<EntityId> WorkersToIdle,
    EquatableArray<TreeEconomyState> TreeUpdates,
    EquatableArray<IDomainEventPayload> ProposedEvents);
