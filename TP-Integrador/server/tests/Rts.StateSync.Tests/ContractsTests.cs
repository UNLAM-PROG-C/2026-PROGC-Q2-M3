using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;
using Rts.StateSync.Projection;
using Rts.StateSync.Visibility;

namespace Rts.StateSync.Tests;

public class ContractsTests
{
  [Fact]
  public void Config_ApprovedValuesGiveTwoTickInterval()
  {
    var config = new StateSyncConfig();

    Assert.Equal(10, config.PositionRateHz);
    Assert.Equal(2, config.GetPositionIntervalTicks(TickRate.Default));
  }

  [Fact]
  public void Config_IntervalFollowsConfigurableTickRate()
  {
    // O-007 / D-062: la cadencia se deriva del tick rate, no de la constante 20.
    Assert.Equal(3, new StateSyncConfig().GetPositionIntervalTicks(new TickRate(30)));
  }

  [Theory]
  [InlineData(3, 3)]   // 20 no es múltiplo de 3.
  [InlineData(0, 3)]
  [InlineData(40, 3)]  // Más rápido que los ticks.
  [InlineData(10, 7)]  // Demasiados decimales.
  public void Config_RejectsInvalidCombinations(int rateHz, int decimals)
  {
    var config = new StateSyncConfig { PositionRateHz = rateHz, PositionDecimals = decimals };

    Assert.NotEmpty(config.GetValidationErrors(TickRate.Default));
    Assert.Throws<ArgumentException>(() => config.GetPositionIntervalTicks(TickRate.Default));
  }

  [Fact]
  public void Audience_AllPlayersOrOnlyOne()
  {
    EventAudience all = EventAudience.AllPlayers;
    EventAudience onlyP2 = EventAudience.Only(PlayerId.Two);

    Assert.True(all.IsForAllPlayers);
    Assert.True(all.Includes(PlayerId.One) && all.Includes(PlayerId.Two));
    Assert.False(onlyP2.Includes(PlayerId.One));
    Assert.True(onlyP2.Includes(PlayerId.Two));
    Assert.Throws<ArgumentException>(() => EventAudience.Only(new PlayerId(0)));
  }

  [Fact]
  public void InitialStateRequest_RequiresValidRecipient()
  {
    var world = new WorldView(TickId.Zero, new MapView(new GridSpec(1.0, 10, 10), MapRevision.Initial), default, default, default, default, default);

    Assert.Throws<ArgumentException>(() => new InitialStateRequest(
        SessionId.New(), InitializationId.First, new PlayerId(3), world, CatalogFixture.Create()));
  }
}

/// <summary>Catálogo válido SOLO para pruebas; los números son suposiciones temporales.</summary>
internal static class CatalogFixture
{
  public static GameCatalog Create() => new(
      TickRate.Default,
      [
          new UnitCatalogEntry(UnitType.Worker, 40, new ResourceAmount(0, 50), 200, 3.0, null),
            new UnitCatalogEntry(UnitType.Soldier, 80, new ResourceAmount(0, 80), 300, 3.5, new AttackStatsView(8, 1.5, 20)),
      ],
      [
          new BuildingCatalogEntry(BuildingType.MainCenter, 1000, new FootprintSize(4, 4), null, null),
            new BuildingCatalogEntry(BuildingType.Farm, 200, new FootprintSize(2, 2), new ResourceAmount(60, 0), null),
            new BuildingCatalogEntry(BuildingType.Barracks, 400, new FootprintSize(3, 3), new ResourceAmount(150, 0), null),
            new BuildingCatalogEntry(BuildingType.Tower, 300, new FootprintSize(2, 2), new ResourceAmount(120, 0), new AttackStatsView(10, 6.0, 30)),
            new BuildingCatalogEntry(BuildingType.Fence, 100, new FootprintSize(1, 1), new ResourceAmount(5, 0), null),
      ],
      farmRepairCost: new ResourceAmount(30, 0),
      farmRepairWorkRequired: 100,
      gatherRange: 1.5,
      buildRange: 1.5);
}
