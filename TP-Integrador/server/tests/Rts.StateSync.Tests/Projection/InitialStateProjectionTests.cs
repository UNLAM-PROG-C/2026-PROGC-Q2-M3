using Rts.Protocol.State;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;
using Rts.StateSync.Projection;

namespace Rts.StateSync.Tests.Projection;

public class InitialStateProjectionTests
{
  private static readonly SessionId Session = SessionId.New();
  private static readonly TickId WorldTick = new(5);

  [Fact]
  public void BuildInitialState_CopiesHeaderAndMap()
  {
    InitialStateDto dto = Build(PlayerId.Two);

    Assert.Equal(Session.ToString(), dto.SessionId);
    Assert.Equal(InitializationId.First.Value, dto.InitializationId);
    Assert.Equal(PlayerId.Two.Value, dto.PlayerId);
    Assert.Equal(WorldTick.Value, dto.Tick);
    Assert.Equal(new MapDto(1.0, 40, 30, 3), dto.Map);
  }

  [Fact]
  public void BuildInitialState_MapsPublicEntities()
  {
    InitialStateDto dto = Build(PlayerId.One);

    Assert.Equal([new PlayerPublicDto(1, 10), new PlayerPublicDto(2, 20)], dto.Players);
    Assert.Equal(
        [new UnitDto(100, 1, UnitType.Worker, 3.5, 4.25, 40, UnitOrderKind.Move),
         new UnitDto(200, 2, UnitType.Soldier, 30.0, 20.0, 80, UnitOrderKind.Idle)],
        dto.Units);
    Assert.Equal(
        [new BuildingDto(10, 1, BuildingType.MainCenter, new FootprintDto(2, 2, 4, 4), 1000, true, null),
         new BuildingDto(11, 1, BuildingType.Farm, new FootprintDto(8, 2, 2, 2), 200, true, new FarmDto(true, false)),
         new BuildingDto(20, 2, BuildingType.MainCenter, new FootprintDto(34, 24, 4, 4), 1000, true, null)],
        dto.Buildings);
    Assert.Equal([new TreeDto(50, 15, 10, 15.5, 10.5, false)], dto.Trees);
  }

  [Fact]
  public void BuildInitialState_PublicWorldIsEqualForBothPlayers()
  {
    InitialStateDto p1 = Build(PlayerId.One);
    InitialStateDto p2 = Build(PlayerId.Two);

    Assert.Equal(p1.Map, p2.Map);
    Assert.Equal(p1.Players, p2.Players);
    Assert.Equal(p1.Units, p2.Units);
    Assert.Equal(p1.Buildings, p2.Buildings);
    Assert.Equal(p1.Trees, p2.Trees);
    Assert.Equal(p1.Catalog.Units, p2.Catalog.Units);
    Assert.Equal(p1.Catalog.Buildings, p2.Catalog.Buildings);
  }

  [Fact]
  public void BuildInitialState_PrivateDataOnlyFromRecipient()
  {
    InitialStateDto p1 = Build(PlayerId.One);
    InitialStateDto p2 = Build(PlayerId.Two);

    Assert.Equal(new ResourcesDto(100, 50), p1.OwnResources);
    Assert.Equal(new ResourcesDto(70, 0), p2.OwnResources);

    ProducerDto p1Producer = Assert.Single(p1.OwnProduction);
    Assert.Equal(10, p1Producer.ProducerId);
    Assert.Equal(ProducerStatus.Training, p1Producer.Status);
    Assert.Equal(UnitType.Worker, p1Producer.ActiveUnitType);
    Assert.Equal(12, p1Producer.RemainingTicks);
    Assert.Equal([UnitType.Soldier], p1Producer.Pending);

    ProducerDto p2Producer = Assert.Single(p2.OwnProduction);
    Assert.Equal(20, p2Producer.ProducerId);
    Assert.Equal(ProducerStatus.Idle, p2Producer.Status);
    Assert.Empty(p2Producer.Pending);
  }

  [Fact]
  public void BuildInitialState_MapsCatalog()
  {
    GameCatalog fixture = CatalogFixture.Create();
    CatalogDto catalog = Build(PlayerId.One).Catalog;

    Assert.Equal(fixture.TickRate.TicksPerSecond, catalog.TicksPerSecond);
    // Listas esperadas: mismos valores que CatalogFixture (ContractsTests.cs).
    Assert.Equal(
        [new UnitCatalogDto(UnitType.Worker, 40, new ResourcesDto(0, 50), 200, 3.0, null),
         new UnitCatalogDto(UnitType.Soldier, 80, new ResourcesDto(0, 80), 300, 3.5, new AttackDto(8, 1.5, 20))],
        catalog.Units);
    Assert.Equal(
        [new BuildingCatalogDto(BuildingType.MainCenter, 1000, 4, 4, null, null),
         new BuildingCatalogDto(BuildingType.Farm, 200, 2, 2, new ResourcesDto(60, 0), null),
         new BuildingCatalogDto(BuildingType.Barracks, 400, 3, 3, new ResourcesDto(150, 0), null),
         new BuildingCatalogDto(BuildingType.Tower, 300, 2, 2, new ResourcesDto(120, 0), new AttackDto(10, 6.0, 30)),
         new BuildingCatalogDto(BuildingType.Fence, 100, 1, 1, new ResourcesDto(5, 0), null)],
        catalog.Buildings);
    Assert.Equal(new ResourcesDto(fixture.FarmRepairCost.Wood, fixture.FarmRepairCost.Food), catalog.FarmRepairCost);
    Assert.Equal(fixture.FarmRepairWorkRequired, catalog.FarmRepairWorkRequired);
    Assert.Equal(fixture.GatherRange, catalog.GatherRange);
    Assert.Equal(fixture.BuildRange, catalog.BuildRange);
  }

  private static InitialStateDto Build(PlayerId recipient)
  {
    var projector = new StateProjector(new StateSyncConfig(), TickRate.Default);
    return projector.BuildInitialState(
        new InitialStateRequest(Session, InitializationId.First, recipient, World(), CatalogFixture.Create()));
  }

  /// <summary>Mundo chico SOLO para pruebas: dos centros, una granja de P1, una unidad por jugador, un árbol y un productor por jugador.</summary>
  private static WorldView World() => new(
      WorldTick,
      new MapView(new GridSpec(1.0, 40, 30), new MapRevision(3)),
      [new PlayerView(PlayerId.One, new ResourceAmount(100, 50)), new PlayerView(PlayerId.Two, new ResourceAmount(70, 0))],
      [new UnitView(new EntityId(100), PlayerId.One, UnitType.Worker, new PositionXZ(3.5, 4.25), 40, UnitOrderKind.Move),
       new UnitView(new EntityId(200), PlayerId.Two, UnitType.Soldier, new PositionXZ(30.0, 20.0), 80, UnitOrderKind.Idle)],
      [new BuildingView(new EntityId(10), PlayerId.One, BuildingType.MainCenter, new Footprint(new GridCell(2, 2), new FootprintSize(4, 4)), 1000, true, null),
       new BuildingView(new EntityId(11), PlayerId.One, BuildingType.Farm, new Footprint(new GridCell(8, 2), new FootprintSize(2, 2)), 200, true, new FarmPublicState(true, false)),
       new BuildingView(new EntityId(20), PlayerId.Two, BuildingType.MainCenter, new Footprint(new GridCell(34, 24), new FootprintSize(4, 4)), 1000, true, null)],
      [new TreeView(new EntityId(50), new GridCell(15, 10), new PositionXZ(15.5, 10.5), false)],
      [new ProducerView(new EntityId(10), PlayerId.One, ProducerStatus.Training, UnitType.Worker, 12, [UnitType.Soldier]),
       new ProducerView(new EntityId(20), PlayerId.Two, ProducerStatus.Idle, null, null, [])]);
}
