using Rts.Economy.Configuration;
using Rts.Economy.Contracts;
using Rts.Shared.Entities;
using Rts.Shared.Resources;
using static Rts.Economy.Tests.EconomyTestData;

namespace Rts.Economy.Tests;

/// <summary>Pruebas de <see cref="EconomyRules"/>: costos, saldo inicial y evaluación de gastos sin mutar saldos (01 §5).</summary>
public class EconomyRulesTests
{
  private static readonly EconomyRules Rules = new(Config);
  private static readonly ResourceAmount Cost = new(Wood: 10, Food: 10);

  [Fact]
  public void InitialBalanceAndCosts_ComeFromConfiguration()
  {
    Assert.Equal(Config.InitialBalance, Rules.InitialBalance);
    Assert.Equal(ResourceAmount.OfWood(Config.FarmRepairWoodCost), Rules.GetFarmRepairCost());
    foreach ((UnitType type, int food) in Config.UnitFoodCosts)
    {
      Assert.Equal(ResourceAmount.OfFood(food), Rules.GetTrainingCost(type));
    }

    foreach ((BuildingType type, int wood) in Config.BuildingWoodCosts)
    {
      Assert.True(Rules.TryGetBuildingCost(type, out ResourceAmount cost));
      Assert.Equal(ResourceAmount.OfWood(wood), cost);
    }
  }

  [Fact]
  public void MainCenter_HasNoBuildingCost()
  {
    Assert.False(Rules.TryGetBuildingCost(BuildingType.MainCenter, out ResourceAmount cost));
    Assert.Equal(ResourceAmount.Zero, cost);
  }

  [Fact]
  public void ExactBalance_IsAffordableAndLeavesZero()
  {
    SpendEvaluation result = Rules.EvaluateSpend(Cost, Cost);

    Assert.True(result.IsAffordable);
    Assert.Null(result.Error);
    Assert.Equal(ResourceAmount.Zero, result.BalanceAfter);
  }

  [Fact]
  public void AffordableSpend_ReturnsBalanceAfterCost()
  {
    var extra = new ResourceAmount(Wood: 3, Food: 4);

    SpendEvaluation result = Rules.EvaluateSpend(Cost + extra, Cost);

    Assert.True(result.IsAffordable);
    Assert.Equal(extra, result.BalanceAfter);
    Assert.True(Rules.CanAfford(Cost + extra, Cost));
  }

  [Theory]
  [InlineData(9, 10, "Economy.InsufficientWood")]
  [InlineData(10, 9, "Economy.InsufficientFood")]
  [InlineData(9, 9, "Economy.InsufficientResources")]
  public void InsufficientBalance_NamesTheMissingResourceAndKeepsBalance(int wood, int food, string expectedError)
  {
    var balance = new ResourceAmount(wood, food);

    SpendEvaluation result = Rules.EvaluateSpend(balance, Cost);

    Assert.False(result.IsAffordable);
    Assert.Equal(expectedError, result.Error?.Value);
    Assert.Equal(balance, result.BalanceAfter);
    Assert.False(Rules.CanAfford(balance, Cost));
  }

  [Fact]
  public void NegativeCost_IsRejected()
  {
    Assert.Throws<ArgumentException>(() => Rules.EvaluateSpend(Cost, ResourceAmount.OfWood(-1)));
  }

  [Fact]
  public void InvalidConfiguration_IsRejected()
  {
    EconomyConfig invalid = Config with { WoodPerGather = 0 };

    Assert.Throws<ArgumentException>(() => new EconomyRules(invalid));
  }

  [Fact]
  public void CostTable_IsCopied_SoLaterChangesToTheConfigDoNotLeakIn()
  {
    var costs = new Dictionary<BuildingType, int>(Config.BuildingWoodCosts);
    var rules = new EconomyRules(Config with { BuildingWoodCosts = costs });
    int original = costs[BuildingType.Tower];

    costs[BuildingType.Tower] = original + Config.FarmRepairWoodCost;

    Assert.True(rules.TryGetBuildingCost(BuildingType.Tower, out ResourceAmount cost));
    Assert.Equal(ResourceAmount.OfWood(original), cost);
  }
}
