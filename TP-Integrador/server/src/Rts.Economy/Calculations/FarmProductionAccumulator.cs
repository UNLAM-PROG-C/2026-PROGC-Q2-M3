using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;

namespace Rts.Economy.Calculations;

/// <summary>
/// Colecciones de trabajo de una sola llamada a <see cref="FarmProductionCalculator.Calculate"/>. Es local a esa llamada,
/// así que el cálculo sigue siendo una función pura.
/// </summary>
internal sealed class FarmProductionAccumulator
{
  public List<FoodProduced> FoodProduced { get; } = [];

  public List<FarmEconomyState> FarmUpdates { get; } = [];

  public List<IDomainEventPayload> Events { get; } = [];

  /// <summary>Las granjas se recorren por <c>FarmId</c>, así que las tres colecciones ya están en el orden del contrato.</summary>
  public FarmProductionOutcome ToOutcome() =>
      new(FoodProduced.ToEquatableArray(), FarmUpdates.ToEquatableArray(), Events.ToEquatableArray());
}
