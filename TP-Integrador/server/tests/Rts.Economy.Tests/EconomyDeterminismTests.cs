using Rts.Economy.Calculations;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using static Rts.Economy.Tests.EconomyTestData;

namespace Rts.Economy.Tests;

/// <summary>
/// Determinismo de Economy (contrato 01 §7): el resultado no depende del orden de las colecciones de entrada ni de qué
/// subcálculo termina primero cuando corren en paralelo.
/// </summary>
public class EconomyDeterminismTests
{
  private const int LastBatch = 1;

  [Fact]
  public void Result_DoesNotDependOnInputOrder()
  {
    EconomyTickResult forward = Calculate(BusyTick(reversed: false));

    Assert.Equal(forward, Calculate(BusyTick(reversed: true)));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Result_DoesNotDependOnWhichSubcalculationFinishesFirst(bool gatheringFinishesFirst)
  {
    EconomyTickInput input = BusyTick(reversed: false);
    var order = new CompletionOrder();
    var calculator = new EconomyCalculator(
        new GatedGathering(new GatheringCalculator(Config), order, gatheringFinishesFirst),
        new GatedFarmProduction(new FarmProductionCalculator(Config), order, !gatheringFinishesFirst),
        runSubcalculationsInParallel: true);

    EconomyTickResult result = calculator.CalculateTick(input);

    Assert.Equal(ExpectedFinishOrder(gatheringFinishesFirst), order.Finished);
    Assert.Equal(Calculate(input), result);
  }

  [Fact]
  public void ParallelAndSequentialModes_GiveTheSameResult()
  {
    EconomyTickInput input = BusyTick(reversed: false);

    Assert.Equal(Calculate(input), new EconomyCalculator(Config, runSubcalculationsInParallel: true).CalculateTick(input));
  }

  [Fact]
  public void Consolidation_SumsPerPlayer_OrdersByPlayer_AndPutsTreeEventsBeforeFarmEvents()
  {
    EconomyTickResult result = Calculate(BusyTick(reversed: false));

    Assert.Equal(new[] { PlayerId.One, PlayerId.Two }, result.PlayerDeltas.Select(d => d.Player));
    Assert.Equal(new ResourceAmount(Config.WoodPerGather, Config.FoodPerFarmBatch), result.PlayerDeltas[0].Delta);
    Assert.Equal(new IDomainEventPayload[] { new TreeDepleted(TreeB), new FarmDepleted(FarmB) }, result.ProposedEvents);
  }

  /// <summary>
  /// Un tick con trabajo en los dos subcálculos: P1 recolecta del árbol A y su granja produce; P2 agota el árbol B y su
  /// granja produce la última tanda. Hay eventos de árboles y de granjas, así que el orden de consolidación importa.
  /// </summary>
  private static EconomyTickInput BusyTick(bool reversed)
  {
    GathererState[] gatherers = [ReadyGatherer(Worker1, TreeA), ReadyGatherer(Worker2, TreeB, PlayerId.Two)];
    TreeEconomyState[] trees =
        [TreeEconomyState.Available(TreeA, Config.MaxWoodPerTree), TreeEconomyState.Available(TreeB, Config.WoodPerGather)];
    FarmEconomyState[] farms =
    [
      Farm(FarmA, PlayerId.One, Config.BatchesPerFarmCycle, OneTickBeforeBatch, Now),
      Farm(FarmB, PlayerId.Two, LastBatch, OneTickBeforeBatch, Now),
    ];
    return reversed
        ? Input(Now, [.. gatherers.Reverse()], [.. trees.Reverse()], [.. farms.Reverse()])
        : Input(Now, [.. gatherers], [.. trees], [.. farms]);
  }

  private static string[] ExpectedFinishOrder(bool gatheringFinishesFirst) => gatheringFinishesFirst
      ? [GatedGathering.Name, GatedFarmProduction.Name]
      : [GatedFarmProduction.Name, GatedGathering.Name];
}
