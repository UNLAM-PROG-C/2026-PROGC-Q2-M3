using Rts.Economy.Model;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.World;
using Rts.Simulation.Initialization;
using Rts.Simulation.Map;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;

namespace Rts.Simulation.Tests.Initialization;

public class InitialWorldFactoryTests
{
  private const int MaxWoodPerTree = 100;
  private const int TreeCount = 2;
  private const int EntitiesPerPlayer = 3; // Centro Principal + 2 unidades.

  private static readonly GridSpec Grid = new(1.0, 40, 40);
  private static readonly GridCell FirstTree = new(10, 20);
  private static readonly GridCell SecondTree = new(29, 19);
  private static readonly GridCell P1Anchor = new(2, 2);
  private static readonly GridCell P2Anchor = new(34, 34);
  private static readonly PositionXZ P1WorkerPosition = new(7.5, 3.5);
  private static readonly PositionXZ P1SoldierPosition = new(7.5, 4.5);
  private static readonly PositionXZ P2WorkerPosition = new(32.5, 36.5);
  private static readonly PositionXZ P2SoldierPosition = new(32.5, 35.5);

  // P2 aparece primero en el archivo a propósito: los IDs igual deben ir árboles → P1 → P2.
  private static readonly MapDefinition Map = new(
      Grid,
      [FirstTree, SecondTree],
      [
        new PlayerStartDefinition(PlayerId.Two, P2Anchor,
        [
          new InitialUnitDefinition(UnitType.Worker, P2WorkerPosition),
          new InitialUnitDefinition(UnitType.Soldier, P2SoldierPosition),
        ]),
        new PlayerStartDefinition(PlayerId.One, P1Anchor,
        [
          new InitialUnitDefinition(UnitType.Worker, P1WorkerPosition),
          new InitialUnitDefinition(UnitType.Soldier, P1SoldierPosition),
        ]),
      ]);

  private static WorldState CreateWorld() =>
      new InitialWorldFactory(Map, MaxWoodPerTree, new FakeEconomyRules(), new FakeConstructionRules(), new FakeCombatRules()).Create();

  private static EntityId Id(int value) => new(value);

  [Fact]
  public void Create_AssignsIdsTreesThenP1ThenP2()
  {
    WorldState world = CreateWorld();
    int p1Center = TreeCount + 1;
    int p2Center = p1Center + EntitiesPerPlayer;

    Assert.Equal([Id(1), Id(2)], world.Trees.Keys);
    Assert.Equal([Id(p1Center), Id(p2Center)], world.Buildings.Keys);
    Assert.Equal(PlayerId.One, world.Buildings[Id(p1Center)].Owner);
    Assert.Equal(PlayerId.Two, world.Buildings[Id(p2Center)].Owner);
    Assert.Equal([Id(p1Center + 1), Id(p1Center + 2), Id(p2Center + 1), Id(p2Center + 2)], world.Units.Keys);
    Assert.Equal(Id(TreeCount + (EntitiesPerPlayer * 2)), world.LastEntityId);
  }

  [Fact]
  public void Create_StartsAtTickZeroWithEqualBalancesAndNothingElse()
  {
    WorldState world = CreateWorld();

    Assert.Equal(TickId.Zero, world.Tick);
    Assert.Equal(MapRevision.Initial, world.MapRevision);
    Assert.Equal(Grid, world.Grid);
    Assert.All(world.Players.Values, p => Assert.Equal(FakeEconomyRules.TestInitialBalance, p.Resources));
    Assert.Equal(OrderId.None, world.LastOrderId);
    Assert.Equal(0, world.Idempotency.Count);
    Assert.False(world.IsGameplayOver);
  }

  [Fact]
  public void Create_TreesAreAvailableWithFullWoodAtCellCenter()
  {
    TreeEntity tree = CreateWorld().Trees[Id(1)];

    Assert.Equal(FirstTree, tree.Cell);
    Assert.Equal(Grid.CellCenter(FirstTree), tree.Position);
    Assert.Equal(TreeStatus.Available, tree.Economy.Status);
    Assert.Equal(MaxWoodPerTree, tree.Economy.WoodRemaining);
  }

  [Fact]
  public void Create_MainCenterIsCompletedOperationalAndIdleProducer()
  {
    BuildingEntity center = CreateWorld().Buildings.Values.Single(b => b.Owner == PlayerId.One);

    Assert.Equal(BuildingType.MainCenter, center.Type);
    Assert.Equal(new Footprint(P1Anchor, FakeConstructionRules.MainCenterSize), center.Footprint);
    Assert.Equal(FakeConstructionRules.MainCenterMaxHp, center.Hp);
    Assert.Equal(TickId.Zero, center.OperationalFromTick);
    Assert.NotNull(center.Production);
    Assert.Equal(ProducerStatus.Idle, center.Production.Status);
  }

  [Fact]
  public void Create_UnitsAreIdleWithMaxHpAndMapPositions()
  {
    UnitEntity[] p1Units = [.. CreateWorld().Units.Values.Where(u => u.Owner == PlayerId.One)];

    Assert.Equal([UnitType.Worker, UnitType.Soldier], p1Units.Select(u => u.Type));
    Assert.Equal([P1WorkerPosition, P1SoldierPosition], p1Units.Select(u => u.Position));
    Assert.Equal([FakeCombatRules.WorkerMaxHp, FakeCombatRules.SoldierMaxHp], p1Units.Select(u => u.Hp));
    Assert.All(p1Units, u => Assert.True(u.Order.IsIdle));
    Assert.All(p1Units, u => Assert.Equal(TickId.Zero, u.ReadyAtTick));
  }

  [Fact]
  public void Create_TwiceGivesTheSameWorld()
  {
    // Se puede repetir en cada nueva preparación de Starting (07 §4) y siempre da lo mismo.
    Assert.Equal(WorldViewProjector.Project(CreateWorld()), WorldViewProjector.Project(CreateWorld()));
  }

  [Fact]
  public void Project_CopiesEveryEntityAndOnlyProducersAsProducerViews()
  {
    WorldState world = CreateWorld();

    WorldView view = WorldViewProjector.Project(world);

    Assert.Equal(world.Tick, view.Tick);
    Assert.Equal(new MapView(Grid, MapRevision.Initial), view.Map);
    Assert.Equal(world.Units.Keys, view.Units.Select(u => u.UnitId));
    Assert.Equal(world.Buildings.Keys, view.Buildings.Select(b => b.BuildingId));
    Assert.Equal(world.Trees.Keys, view.Trees.Select(t => t.TreeId));
    Assert.All(view.Trees, t => Assert.True(t.IsAvailable));
    Assert.All(view.Buildings, b => Assert.Null(b.Farm));
    Assert.Equal(world.Buildings.Keys, view.Producers.Select(p => p.ProducerId));
    Assert.All(view.Producers, p => Assert.Equal(ProducerStatus.Idle, p.Status));
  }
}
