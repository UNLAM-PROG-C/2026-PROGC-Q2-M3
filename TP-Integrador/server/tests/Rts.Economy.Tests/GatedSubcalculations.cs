using Rts.Economy.Calculations;
using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Economy.Tests;

/// <summary>Envuelve el subcálculo real de recolección y lo hace terminar primero o segundo.</summary>
internal sealed class GatedGathering(IGatheringCalculation inner, CompletionOrder order, bool finishesFirst) : IGatheringCalculation
{
  public const string Name = "Gathering";

  public GatheringOutcome Calculate(TickId tick, EquatableArray<GathererState> gatherers, EquatableArray<TreeEconomyState> trees) =>
      order.Run(Name, finishesFirst, () => inner.Calculate(tick, gatherers, trees));
}

/// <summary>Envuelve el subcálculo real de granjas y lo hace terminar primero o segundo.</summary>
internal sealed class GatedFarmProduction(IFarmProductionCalculation inner, CompletionOrder order, bool finishesFirst)
    : IFarmProductionCalculation
{
  public const string Name = "FarmProduction";

  public FarmProductionOutcome Calculate(TickId tick, EquatableArray<FarmEconomyState> farms) =>
      order.Run(Name, finishesFirst, () => inner.Calculate(tick, farms));
}
