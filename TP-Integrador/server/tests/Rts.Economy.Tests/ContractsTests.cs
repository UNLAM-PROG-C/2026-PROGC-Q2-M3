using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Resources;

namespace Rts.Economy.Tests;

public class ContractsTests
{
  private static readonly TickId Tick = new(50);
  private static readonly EntityId TreeA = new(100);
  private static readonly EntityId TreeB = new(101);

  [Fact]
  public void TickInput_AcceptsConsistentSnapshot()
  {
    var input = new EconomyTickInput(
        Tick,
        [new GathererState(new EntityId(1), PlayerId.One, TreeA, 0), new GathererState(new EntityId(2), PlayerId.Two, TreeA, 3)],
        [TreeEconomyState.Available(TreeA, 12), TreeEconomyState.Depleted(TreeB, new TickId(10))],
        [new FarmEconomyState(new EntityId(200), PlayerId.One, FarmProductionStatus.Producing, 5, 0, new TickId(11))]);

    Assert.Equal(2, input.Gatherers.Count);
    Assert.Equal(Tick, input.Tick);
  }

  [Fact]
  public void TickInput_RejectsDuplicateIds()
  {
    Assert.Throws<ArgumentException>(() => new EconomyTickInput(
        Tick,
        default,
        [TreeEconomyState.Available(TreeA, 12), TreeEconomyState.Available(TreeA, 5)],
        default));

    Assert.Throws<ArgumentException>(() => new EconomyTickInput(
        Tick,
        [new GathererState(new EntityId(1), PlayerId.One, TreeA, 0), new GathererState(new EntityId(1), PlayerId.One, TreeA, 0)],
        [TreeEconomyState.Available(TreeA, 12)],
        default));
  }

  [Fact]
  public void TickInput_RejectsGathererOnUnknownTree()
  {
    Assert.Throws<ArgumentException>(() => new EconomyTickInput(
        Tick,
        [new GathererState(new EntityId(1), PlayerId.One, TreeB, 0)],
        [TreeEconomyState.Available(TreeA, 12)],
        default));
  }

  [Fact]
  public void TickInput_EqualContentIsEqual()
  {
    EconomyTickInput Build() => new(
        Tick,
        [new GathererState(new EntityId(1), PlayerId.One, TreeA, 0)],
        [TreeEconomyState.Available(TreeA, 12)],
        default);

    Assert.Equal(Build(), Build());
  }

  [Fact]
  public void TickResult_EqualContentIsEqual_ForDeterminismTests()
  {
    EconomyTickResult Build() => EconomyTickResult.Empty(Tick) with
    {
      PlayerDeltas = [new PlayerResourceDelta(PlayerId.One, ResourceAmount.OfWood(5))],
      WoodGathered = [new WoodGathered(new EntityId(1), PlayerId.One, TreeA, 5)],
      WorkersToIdle = [new EntityId(2)],
      ProposedEvents = [new TreeDepleted(TreeA)],
    };

    Assert.Equal(Build(), Build());
    Assert.NotEqual(Build(), EconomyTickResult.Empty(Tick));
  }

  [Fact]
  public void SpendEvaluation_AffordableKeepsNonNegativeBalance()
  {
    SpendEvaluation ok = SpendEvaluation.Affordable(new ResourceAmount(3, 0));

    Assert.True(ok.IsAffordable);
    Assert.Null(ok.Error);
    Assert.Throws<ArgumentException>(() => SpendEvaluation.Affordable(new ResourceAmount(-1, 0)));
  }

  [Fact]
  public void SpendEvaluation_InsufficientKeepsBalanceAndRequiresError()
  {
    var balance = new ResourceAmount(2, 0);
    SpendEvaluation rejected = SpendEvaluation.Insufficient(balance, EconomyErrors.InsufficientWood);

    Assert.False(rejected.IsAffordable);
    Assert.Equal(balance, rejected.BalanceAfter);
    Assert.Equal(EconomyErrors.InsufficientWood, rejected.Error);
    Assert.Throws<ArgumentException>(() => SpendEvaluation.Insufficient(balance, default));
  }

  [Fact]
  public void EconomyErrors_UseEconomyArea()
  {
    Assert.All(
        [EconomyErrors.InsufficientWood, EconomyErrors.InsufficientFood, EconomyErrors.InsufficientResources],
        code => Assert.Equal("Economy", code.Area));
  }
}
