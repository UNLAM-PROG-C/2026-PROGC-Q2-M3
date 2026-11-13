using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using static Rts.Economy.Tests.EconomyTestData;

namespace Rts.Economy.Tests;

/// <summary>Pruebas de recolección y ciclo de vida de árboles (contrato 01 §4, reglas 1 a 7).</summary>
public class GatheringTests
{
  [Fact]
  public void FirstWood_ArrivesAfterAFullIntervalInRange()
  {
    GathererState worker = Gatherer(Worker1, TreeA, ticksAccumulated: 0);
    TreeEconomyState tree = TreeEconomyState.Available(TreeA, Config.MaxWoodPerTree);
    for (int elapsed = OneTick; elapsed < Config.GatherIntervalTicks; elapsed++)
    {
      EconomyTickResult waiting = Calculate(Input(Now.Plus(elapsed), [worker], [tree]));
      Assert.Empty(waiting.WoodGathered);
      worker = Assert.Single(waiting.GathererUpdates);
      Assert.Equal(elapsed, worker.TicksAccumulated);
    }

    EconomyTickResult gathered = Calculate(Input(Now.Plus(Config.GatherIntervalTicks), [worker], [tree]));
    Assert.Equal(Config.WoodPerGather, Assert.Single(gathered.WoodGathered).Amount);
    Assert.Equal(0, Assert.Single(gathered.GathererUpdates).TicksAccumulated);
  }

  [Fact]
  public void Gather_SubtractsWoodFromTreeAndCreditsOwner()
  {
    TreeEconomyState tree = TreeEconomyState.Available(TreeA, Config.MaxWoodPerTree);

    EconomyTickResult result = Calculate(Input(Now, [ReadyGatherer(Worker1, TreeA, PlayerId.Two)], [tree]));

    Assert.Equal(Config.MaxWoodPerTree - Config.WoodPerGather, Assert.Single(result.TreeUpdates).WoodRemaining);
    PlayerResourceDelta delta = Assert.Single(result.PlayerDeltas);
    Assert.Equal(PlayerId.Two, delta.Player);
    Assert.Equal(ResourceAmount.OfWood(Config.WoodPerGather), delta.Delta);
  }

  [Fact]
  public void SimultaneousRequests_AreServedByWorkerIdWithPartialRemainder()
  {
    int remainder = Config.MaxWoodPerTree - (2 * Config.WoodPerGather);
    EconomyTickInput input = Input(
        Now,
        [ReadyGatherer(Worker3, TreeA), ReadyGatherer(Worker1, TreeA), ReadyGatherer(Worker2, TreeA)],
        [TreeEconomyState.Available(TreeA, Config.MaxWoodPerTree)]);

    EconomyTickResult result = Calculate(input);

    Assert.Equal(new[] { Worker1, Worker2, Worker3 }, result.WoodGathered.Select(w => w.WorkerId));
    Assert.Equal(new[] { Config.WoodPerGather, Config.WoodPerGather, remainder }, result.WoodGathered.Select(w => w.Amount));
    Assert.Equal(ResourceAmount.OfWood(Config.MaxWoodPerTree), Assert.Single(result.PlayerDeltas).Delta);
  }

  [Fact]
  public void WorkersThatGetNothing_LeaveNoRecord()
  {
    int twoGathers = 2 * Config.WoodPerGather;
    EconomyTickInput input = Input(
        Now,
        [ReadyGatherer(Worker1, TreeA), ReadyGatherer(Worker2, TreeA), ReadyGatherer(Worker3, TreeA), ReadyGatherer(Worker4, TreeA)],
        [TreeEconomyState.Available(TreeA, twoGathers)]);

    EconomyTickResult result = Calculate(input);

    Assert.Equal(new[] { Worker1, Worker2 }, result.WoodGathered.Select(w => w.WorkerId));
    Assert.Equal(new[] { Worker1, Worker2, Worker3, Worker4 }, result.WorkersToIdle);
  }

  [Fact]
  public void DepletedTree_SendsAllItsGatherersToIdle_EvenThoseMidInterval()
  {
    EconomyTickInput input = Input(
        Now,
        [ReadyGatherer(Worker1, TreeA), Gatherer(Worker2, TreeA, ticksAccumulated: 0)],
        [TreeEconomyState.Available(TreeA, Config.WoodPerGather)]);

    EconomyTickResult result = Calculate(input);

    Assert.Equal(new[] { Worker1, Worker2 }, result.WorkersToIdle);
    Assert.Empty(result.GathererUpdates);
    Assert.Equal(TreeEconomyState.Depleted(TreeA, Now), Assert.Single(result.TreeUpdates));
    Assert.Equal(new TreeDepleted(TreeA), Assert.Single(result.ProposedEvents));
  }

  [Fact]
  public void GathererOnAlreadyDepletedTree_GoesIdleWithoutWood()
  {
    EconomyTickInput input = Input(Now, [ReadyGatherer(Worker1, TreeA)], [TreeEconomyState.Depleted(TreeA, Now)]);

    EconomyTickResult result = Calculate(input);

    Assert.Empty(result.WoodGathered);
    Assert.Equal(new[] { Worker1 }, result.WorkersToIdle);
    Assert.Empty(result.PlayerDeltas);
  }

  [Fact]
  public void DepletedTree_DoesNotRespawnBeforeItsTick()
  {
    TreeEconomyState depleted = TreeEconomyState.Depleted(TreeA, Now);

    EconomyTickResult result = Calculate(Input(Now.Plus(Config.TreeRespawnTicks - OneTick), trees: [depleted]));

    Assert.Empty(result.TreeUpdates);
    Assert.Empty(result.ProposedEvents);
  }

  [Fact]
  public void DepletedTree_RespawnsWithSameIdAndFullWood_ButNobodyGathersThatTick()
  {
    TreeEconomyState depleted = TreeEconomyState.Depleted(TreeA, Now);
    TickId respawnTick = Now.Plus(Config.TreeRespawnTicks);

    EconomyTickResult result = Calculate(Input(respawnTick, [ReadyGatherer(Worker1, TreeA)], [depleted]));

    Assert.Equal(TreeEconomyState.Available(TreeA, Config.MaxWoodPerTree), Assert.Single(result.TreeUpdates));
    Assert.Equal(new TreeRespawned(TreeA), Assert.Single(result.ProposedEvents));
    Assert.Empty(result.WoodGathered);
    Assert.Equal(new[] { Worker1 }, result.WorkersToIdle);
  }

  [Fact]
  public void TreeWithoutGatherers_DoesNotChange()
  {
    EconomyTickResult result = Calculate(Input(Now, trees: [TreeEconomyState.Available(TreeA, Config.MaxWoodPerTree)]));

    Assert.Equal(EconomyTickResult.Empty(Now), result);
  }
}
