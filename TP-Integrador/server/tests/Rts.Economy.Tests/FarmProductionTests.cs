using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using static Rts.Economy.Tests.EconomyTestData;

namespace Rts.Economy.Tests;

/// <summary>Pruebas de producción de granjas (contrato 01 §4, reglas 8 a 11; D-014).</summary>
public class FarmProductionTests
{
  [Fact]
  public void Farm_DoesNotAdvanceBeforeProductionActiveFromTick()
  {
    FarmEconomyState farm = FreshFarm(FarmA, PlayerId.One, activeFrom: Now.Next());

    Assert.Equal(EconomyTickResult.Empty(Now), Calculate(Input(Now, farms: [farm])));
    Assert.Equal(OneTick, Assert.Single(Calculate(Input(Now.Next(), farms: [farm])).FarmUpdates).TicksAccumulated);
  }

  [Fact]
  public void Farm_ProducesOneBatchWhenItsIntervalCompletes()
  {
    FarmEconomyState farm = Farm(FarmA, PlayerId.Two, Config.BatchesPerFarmCycle, OneTickBeforeBatch, Now);

    EconomyTickResult result = Calculate(Input(Now, farms: [farm]));

    Assert.Equal(new FoodProduced(FarmA, PlayerId.Two, Config.FoodPerFarmBatch), Assert.Single(result.FoodProduced));
    FarmEconomyState updated = Assert.Single(result.FarmUpdates);
    Assert.Equal(Config.BatchesPerFarmCycle - OneTick, updated.BatchesRemaining);
    Assert.Equal(0, updated.TicksAccumulated);
    Assert.Equal(ResourceAmount.OfFood(Config.FoodPerFarmBatch), Assert.Single(result.PlayerDeltas).Delta);
  }

  [Fact]
  public void Farm_ProducesAFullCycleOfBatchesThenDepletesWithoutDisappearing()
  {
    FarmEconomyState farm = FreshFarm(FarmA, PlayerId.One, activeFrom: Now);
    int ticksForFullCycle = Config.BatchesPerFarmCycle * Config.FarmProductionIntervalTicks;
    var results = new List<EconomyTickResult>();
    for (int elapsed = 0; elapsed < ticksForFullCycle; elapsed++)
    {
      results.Add(Calculate(Input(Now.Plus(elapsed), farms: [farm])));
      farm = Assert.Single(results[^1].FarmUpdates);
    }

    Assert.Equal(Config.BatchesPerFarmCycle * Config.FoodPerFarmBatch, results.SelectMany(r => r.FoodProduced).Sum(f => f.Amount));
    Assert.Equal(FarmProductionStatus.Depleted, farm.Status);
    Assert.Equal(new FarmDepleted(FarmA), Assert.Single(results.SelectMany(r => r.ProposedEvents)));
  }

  [Fact]
  public void DepletedFarm_DoesNotChangeInEconomy()
  {
    var depleted = new FarmEconomyState(FarmA, PlayerId.One, FarmProductionStatus.Depleted, 0, 0, Now);

    Assert.Equal(EconomyTickResult.Empty(Now), Calculate(Input(Now, farms: [depleted])));
  }
}
