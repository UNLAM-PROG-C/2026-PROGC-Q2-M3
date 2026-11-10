using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production.Tests;

public class ModelTests
{
  private static readonly EntityId Center = new(1);
  private static readonly Footprint CenterFootprint = new(new GridCell(4, 4), new FootprintSize(4, 4));

  private static ProductionRequest WorkerRequest(long sequence) =>
      new(new CommandSequence(sequence), Center, PlayerId.One, UnitType.Worker);

  private static ProducerState CenterWith(ProductionJob? active, params ProductionRequest[] pending) =>
      new(Center, PlayerId.One, BuildingType.MainCenter, CenterFootprint, TickId.Zero, active, [.. pending]);

  [Theory]
  [InlineData(BuildingType.MainCenter, UnitType.Worker)]
  [InlineData(BuildingType.Barracks, UnitType.Soldier)]
  public void Catalog_MatchesDecidedProducers(BuildingType building, UnitType unit)
  {
    Assert.Equal(unit, ProducerCatalog.GetProducedUnit(building));
    Assert.True(ProducerCatalog.IsProducer(building));
  }

  [Theory]
  [InlineData(BuildingType.Farm)]
  [InlineData(BuildingType.Tower)]
  [InlineData(BuildingType.Fence)]
  public void Catalog_OtherBuildingsDoNotProduce(BuildingType building)
  {
    Assert.Null(ProducerCatalog.GetProducedUnit(building));
  }

  [Fact]
  public void Status_IsDerivedFromJobAndQueue()
  {
    Assert.Equal(ProducerStatus.Idle, CenterWith(null).Status);
    Assert.Equal(ProducerStatus.WaitingForResources, CenterWith(null, WorkerRequest(3)).Status);
    Assert.Equal(ProducerStatus.Training, CenterWith(new ProductionJob(WorkerRequest(2), 3, TickId.Zero, 5)).Status);
    Assert.Equal(ProducerStatus.PendingSpawn, CenterWith(new ProductionJob(WorkerRequest(2), 3, TickId.Zero, 0)).Status);
  }

  [Fact]
  public void Producer_PendingMustBeFifoBySequence()
  {
    _ = CenterWith(null, WorkerRequest(3), WorkerRequest(8));

    Assert.Throws<ArgumentException>(() => CenterWith(null, WorkerRequest(8), WorkerRequest(3)));
    Assert.Throws<ArgumentException>(() => CenterWith(null, WorkerRequest(3), WorkerRequest(3)));
  }

  [Fact]
  public void Producer_RequestsMustMatchProducerOwnerAndUnitType()
  {
    var soldierForCenter = new ProductionRequest(new CommandSequence(4), Center, PlayerId.One, UnitType.Soldier);
    var otherProducer = new ProductionRequest(new CommandSequence(5), new EntityId(99), PlayerId.One, UnitType.Worker);
    var otherOwner = new ProductionRequest(new CommandSequence(6), Center, PlayerId.Two, UnitType.Worker);

    Assert.Throws<ArgumentException>(() => CenterWith(null, soldierForCenter));
    Assert.Throws<ArgumentException>(() => CenterWith(null, otherProducer));
    Assert.Throws<ArgumentException>(() => CenterWith(new ProductionJob(otherOwner, 3, TickId.Zero, 2)));
  }

  [Fact]
  public void Producer_OnlyProducerBuildings()
  {
    Assert.Throws<ArgumentException>(() =>
        new ProducerState(new EntityId(2), PlayerId.One, BuildingType.Tower, CenterFootprint, TickId.Zero, null, default));
  }

  [Fact]
  public void Producer_OperationalFromTick()
  {
    var barracks = new ProducerState(
        new EntityId(2), PlayerId.One, BuildingType.Barracks, CenterFootprint, new TickId(11), null, default);

    Assert.False(barracks.IsOperationalAt(new TickId(10)));
    Assert.True(barracks.IsOperationalAt(new TickId(11)));
  }

  [Fact]
  public void Request_AndJob_ValidateValues()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        new ProductionRequest(new CommandSequence(0), Center, PlayerId.One, UnitType.Worker));
    Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionJob(WorkerRequest(1), -1, TickId.Zero, 3));
    Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionJob(WorkerRequest(1), 3, TickId.Zero, -1));
  }
}
