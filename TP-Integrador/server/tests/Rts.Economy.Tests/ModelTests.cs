using Rts.Economy.Model;
using Rts.Shared.Ids;

namespace Rts.Economy.Tests;

public class ModelTests
{
  private static readonly EntityId Tree = new(100);
  private static readonly EntityId Farm = new(200);
  private static readonly EntityId Worker = new(1);

  [Fact]
  public void Tree_AvailableRequiresWoodAndNoDepletionTick()
  {
    TreeEconomyState tree = TreeEconomyState.Available(Tree, 12);

    Assert.Equal(TreeStatus.Available, tree.Status);
    Assert.Throws<ArgumentException>(() => TreeEconomyState.Available(Tree, 0));
    Assert.Throws<ArgumentException>(() => new TreeEconomyState(Tree, 5, TreeStatus.Available, new TickId(3)));
  }

  [Fact]
  public void Tree_DepletedHasZeroWoodAndDepletionTick()
  {
    TreeEconomyState tree = TreeEconomyState.Depleted(Tree, new TickId(40));

    Assert.Equal(0, tree.WoodRemaining);
    Assert.Equal(new TickId(40), tree.DepletedAtTick);
    Assert.Throws<ArgumentException>(() => new TreeEconomyState(Tree, 3, TreeStatus.Depleted, new TickId(40)));
    Assert.Throws<ArgumentException>(() => new TreeEconomyState(Tree, 0, TreeStatus.Depleted, null));
  }

  [Fact]
  public void Tree_RejectsInvalidIdAndNegativeWood()
  {
    Assert.Throws<ArgumentException>(() => TreeEconomyState.Available(EntityId.None, 5));
    Assert.Throws<ArgumentOutOfRangeException>(() => new TreeEconomyState(Tree, -1, TreeStatus.Available, null));
  }

  [Fact]
  public void Farm_ProducingRequiresRemainingBatches()
  {
    var farm = new FarmEconomyState(Farm, PlayerId.One, FarmProductionStatus.Producing, 5, 0, new TickId(11));

    Assert.Equal(new TickId(11), farm.ProductionActiveFromTick);
    Assert.Throws<ArgumentException>(() =>
        new FarmEconomyState(Farm, PlayerId.One, FarmProductionStatus.Producing, 0, 0, new TickId(11)));
  }

  [Fact]
  public void Farm_DepletedHasNoBatchesAndResetTimer()
  {
    var farm = new FarmEconomyState(Farm, PlayerId.Two, FarmProductionStatus.Depleted, 0, 0, new TickId(11));

    Assert.Equal(FarmProductionStatus.Depleted, farm.Status);
    Assert.Throws<ArgumentException>(() =>
        new FarmEconomyState(Farm, PlayerId.Two, FarmProductionStatus.Depleted, 1, 0, new TickId(11)));
    Assert.Throws<ArgumentException>(() =>
        new FarmEconomyState(Farm, PlayerId.Two, FarmProductionStatus.Depleted, 0, 4, new TickId(11)));
  }

  [Fact]
  public void Farm_RequiresValidOwner()
  {
    Assert.Throws<ArgumentException>(() =>
        new FarmEconomyState(Farm, new PlayerId(3), FarmProductionStatus.Producing, 5, 0, TickId.Zero));
  }

  [Fact]
  public void Gatherer_ValidatesIdsOwnerAndCounter()
  {
    var gatherer = new GathererState(Worker, PlayerId.One, Tree, 7);

    Assert.Equal(7, gatherer.TicksAccumulated);
    Assert.Throws<ArgumentException>(() => new GathererState(EntityId.None, PlayerId.One, Tree, 0));
    Assert.Throws<ArgumentException>(() => new GathererState(Worker, new PlayerId(0), Tree, 0));
    Assert.Throws<ArgumentException>(() => new GathererState(Worker, PlayerId.One, EntityId.None, 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new GathererState(Worker, PlayerId.One, Tree, -1));
  }

  [Fact]
  public void States_HaveValueEquality()
  {
    Assert.Equal(TreeEconomyState.Available(Tree, 12), TreeEconomyState.Available(Tree, 12));
    Assert.Equal(new GathererState(Worker, PlayerId.One, Tree, 2), new GathererState(Worker, PlayerId.One, Tree, 2));
  }
}
