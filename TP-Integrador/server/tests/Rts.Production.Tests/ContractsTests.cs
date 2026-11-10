using Rts.Production.Configuration;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production.Tests;

public class ContractsTests
{
  private static readonly GridSpec Grid = new(1.0, 30, 30);
  private static readonly SpawnView EmptyView = new(Grid, [], [], []);

  private static readonly ProducerState CenterP1 = new(
      new EntityId(1), PlayerId.One, BuildingType.MainCenter, new Footprint(new GridCell(4, 4), new FootprintSize(4, 4)),
      TickId.Zero, null, default);

  /// <summary>Configuración válida SOLO para pruebas; los números son suposiciones temporales, no balance aprobado.</summary>
  internal static ProductionConfig ValidConfig() => new()
  {
    TrainingDurationTicks = new Dictionary<UnitType, int> { [UnitType.Worker] = 100, [UnitType.Soldier] = 160 },
    MaxPendingPerProducer = 5,
    SpawnSearchRadiusCells = 3,
  };

  [Fact]
  public void Config_ValidHasNoErrors()
  {
    Assert.Empty(ValidConfig().GetValidationErrors());
  }

  [Fact]
  public void Config_RejectsMissingOrNonPositiveValues()
  {
    ProductionConfig config = ValidConfig() with
    {
      TrainingDurationTicks = new Dictionary<UnitType, int> { [UnitType.Worker] = 0 },
      MaxPendingPerProducer = 0,
      SpawnSearchRadiusCells = -1,
    };

    Assert.Equal(4, config.GetValidationErrors().Count);
    Assert.Throws<ArgumentException>(config.Validate);
  }

  [Fact]
  public void TickInput_RequiresFoodForEveryProducerOwner()
  {
    _ = new ProductionTickInput(TickId.Zero, [CenterP1], [new PlayerFood(PlayerId.One, 10)], EmptyView);

    Assert.Throws<ArgumentException>(() =>
        new ProductionTickInput(TickId.Zero, [CenterP1], [new PlayerFood(PlayerId.Two, 10)], EmptyView));
  }

  [Fact]
  public void TickInput_RejectsDuplicates()
  {
    Assert.Throws<ArgumentException>(() =>
        new ProductionTickInput(TickId.Zero, [CenterP1, CenterP1], [new PlayerFood(PlayerId.One, 1)], EmptyView));
    Assert.Throws<ArgumentException>(() =>
        new ProductionTickInput(TickId.Zero, default, [new PlayerFood(PlayerId.One, 1), new PlayerFood(PlayerId.One, 2)], EmptyView));
  }

  [Fact]
  public void PlayerFood_CannotBeNegative()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerFood(PlayerId.One, -1));
    Assert.Throws<ArgumentException>(() => new PlayerFood(new PlayerId(0), 5));
  }

  [Fact]
  public void SpawnView_RejectsCellsOutsideMap()
  {
    Assert.Throws<ArgumentException>(() => new SpawnView(Grid, [], [], [new GridCell(30, 1)]));
  }

  [Fact]
  public void TickResult_EqualContentIsEqual()
  {
    ProductionTickResult Build() => ProductionTickResult.Empty(new TickId(9)) with
    {
      Starts = [new ProductionStart(CenterP1.ProducerId, PlayerId.One, new CommandSequence(4), UnitType.Worker, 3)],
      Spawns = [new SpawnProposal(CenterP1.ProducerId, PlayerId.One, new CommandSequence(2), UnitType.Worker,
                new GridCell(3, 3), Grid.CellCenter(new GridCell(3, 3)))],
      ProposedEvents = [new ProductionChanged(CenterP1.ProducerId, PlayerId.One, ProducerStatus.Training, UnitType.Worker, 100, [])],
    };

    Assert.Equal(Build(), Build());
  }

  [Fact]
  public void ProductionErrors_UseProductionArea()
  {
    Assert.All(
        [ProductionErrors.NotAProducer, ProductionErrors.WrongUnitType, ProductionErrors.ProducerNotOperational, ProductionErrors.QueueFull],
        code => Assert.Equal("Production", code.Area));
  }
}
