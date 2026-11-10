using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Economy.Calculations;

/// <summary>
/// Subcálculo interno de producción de granjas (01 §6-7: <c>FarmProductionCalculation</c>).
/// Puede ejecutarse en paralelo con <see cref="IGatheringCalculation"/>; no comparte estado mutable con él.
/// Incluye: avance del temporizador desde <c>ProductionActiveFromTick</c>, tandas y agotamiento.
/// </summary>
internal interface IFarmProductionCalculation
{
    FarmProductionOutcome Calculate(TickId tick, EquatableArray<FarmEconomyState> farms);
}

/// <summary>Resultado parcial de <see cref="IFarmProductionCalculation"/>; se consolida en <c>EconomyTickResult</c> en orden fijo.</summary>
internal sealed record FarmProductionOutcome(
    EquatableArray<Contracts.FoodProduced> FoodProduced,
    EquatableArray<FarmEconomyState> FarmUpdates,
    EquatableArray<IDomainEventPayload> ProposedEvents);
