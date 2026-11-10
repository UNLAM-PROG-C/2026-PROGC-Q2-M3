using Rts.Economy.Configuration;
using Rts.Shared.Entities;
using Rts.Shared.Resources;

namespace Rts.Economy.Tests;

public class EconomyConfigTests
{
  [Fact]
  public void ValidConfig_HasNoErrors()
  {
    EconomyConfig config = TestConfig.Valid();

    Assert.Empty(config.GetValidationErrors());
    config.Validate();
  }

  [Fact]
  public void FarmDefaults_MatchFunctionalDefinition()
  {
    EconomyConfig config = TestConfig.Valid();

    Assert.Equal(7, config.FoodPerFarmBatch);
    Assert.Equal(5, config.BatchesPerFarmCycle);
  }

  [Fact]
  public void IntervalsAndAmounts_MustBePositive()
  {
    EconomyConfig config = TestConfig.Valid() with
    {
      GatherIntervalTicks = 0,
      TreeRespawnTicks = -1,
      WoodPerGather = 0,
      FoodPerFarmBatch = 0,
    };

    IReadOnlyList<string> errors = config.GetValidationErrors();

    Assert.Equal(4, errors.Count);
    Assert.Throws<ArgumentException>(config.Validate);
  }

  [Fact]
  public void InitialBalance_CannotBeNegative()
  {
    EconomyConfig config = TestConfig.Valid() with { InitialBalance = new ResourceAmount(-1, 0) };

    Assert.Single(config.GetValidationErrors());
  }

  [Fact]
  public void BuildingCosts_MustCoverExactlyBuildableTypes()
  {
    EconomyConfig missingFence = TestConfig.Valid() with
    {
      BuildingWoodCosts = new Dictionary<BuildingType, int>
      {
        [BuildingType.Farm] = 30,
        [BuildingType.Barracks] = 60,
        [BuildingType.Tower] = 50,
      },
    };
    EconomyConfig withMainCenter = TestConfig.Valid() with
    {
      BuildingWoodCosts = new Dictionary<BuildingType, int>(TestConfig.Valid().BuildingWoodCosts)
      {
        [BuildingType.MainCenter] = 100,
      },
    };

    Assert.Contains(missingFence.GetValidationErrors(), e => e.Contains(nameof(BuildingType.Fence)));
    Assert.Contains(withMainCenter.GetValidationErrors(), e => e.Contains(nameof(BuildingType.MainCenter)));
  }

  [Fact]
  public void Costs_CannotBeNegative()
  {
    EconomyConfig config = TestConfig.Valid() with
    {
      FarmRepairWoodCost = -1,
      UnitFoodCosts = new Dictionary<UnitType, int> { [UnitType.Worker] = -3, [UnitType.Soldier] = 5 },
    };

    Assert.Equal(2, config.GetValidationErrors().Count);
  }

  [Fact]
  public void UnitCosts_MustCoverAllUnitTypes()
  {
    EconomyConfig config = TestConfig.Valid() with
    {
      UnitFoodCosts = new Dictionary<UnitType, int> { [UnitType.Worker] = 3 },
    };

    Assert.Contains(config.GetValidationErrors(), e => e.Contains(nameof(UnitType.Soldier)));
  }
}
