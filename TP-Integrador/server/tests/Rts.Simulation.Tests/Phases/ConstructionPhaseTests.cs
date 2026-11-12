using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Economy.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.Phases;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Phases;

public class ConstructionPhaseTests
{
  private const int SiteHp = 100;
  private const int SiteHpAfterProgress = 150;
  private const int RepairWorkSoFar = 3;
  private const int RepairWorkAfterProgress = 4;
  private const int RepairWorkDone = 10;
  private static readonly GridCell SiteAnchor = new(20, 20);
  private static readonly GridCell RepairingFarmAnchor = new(28, 28);
  private static readonly GridCell TowerAnchor = new(12, 24);
  private static readonly GridCell BarracksAnchor = new(16, 28);
  private static readonly PositionXZ NextToSite = new(19.5, 20.5);
  private static readonly PositionXZ NextToRepairingFarm = new(27.5, 28.5);

  /// <summary>
  /// P1: una granja en obra con el trabajador A al lado y el B con la misma orden pero lejos. P2: una granja agotada en reparación,
  /// con su trabajador al lado.
  /// </summary>
  private static (WorldState World, EntityId Site, EntityId RepairingFarm) ConstructionWorld()
  {
    WorldState world = TestWorld.Create();
    (world, EntityId site) = TestWorld.WithSite(world, PlayerId.One, BuildingType.Farm, SiteAnchor, SiteHp);
    (world, EntityId repairing) = TestWorld.WithRepairingFarm(world, PlayerId.Two, RepairingFarmAnchor, RepairWorkSoFar);
    world = TestWorld.WithTaskOrder(world, TestWorld.P1WorkerA, UnitOrderKind.Build, site);
    world = TestWorld.WithUnitAt(world, TestWorld.P1WorkerA, NextToSite);
    world = TestWorld.WithTaskOrder(world, TestWorld.P1WorkerB, UnitOrderKind.Build, site);
    world = TestWorld.WithTaskOrder(world, TestWorld.P2Worker, UnitOrderKind.RepairFarm, repairing);
    world = TestWorld.WithUnitAt(world, TestWorld.P2Worker, NextToRepairingFarm);
    return (world, site, repairing);
  }

  private static ConstructionPhase Phase(FakeConstructionCalculator calculator) =>
      new(calculator, new FakeConstructionRules(), new FakeMovementRules(), TestWorld.FarmBatches);

  private static FakeConstructionCalculator Responding(
      BuildingProgress[]? buildings = null, RepairProgress[]? repairs = null, IDomainEventPayload[]? events = null) =>
      new() { Respond = input => new ConstructionTickResult(input.Tick, [.. buildings ?? []], [.. repairs ?? []], [.. events ?? []]) };

  [Fact]
  public void Input_HasSitesRepairsAndOnlyTheWorkersInRangeOfTheFootprint()
  {
    var (world, site, repairing) = ConstructionWorld();
    var calculator = new FakeConstructionCalculator();

    Phase(calculator).Execute(new TickContext(world));

    ConstructionTickInput input = Assert.Single(calculator.Inputs);
    Assert.Equal([new BuildingUnderConstruction(site, PlayerId.One, BuildingType.Farm, SiteHp)], input.Buildings);
    Assert.Equal([new WorkerContribution(TestWorld.P1WorkerA, site)], input.Builders); // El B camina: no aporta.
    Assert.Equal([new FarmRepairState(repairing, PlayerId.Two, RepairWorkSoFar)], input.Repairs);
    Assert.Equal([new WorkerContribution(TestWorld.P2Worker, repairing)], input.Repairers);
  }

  [Fact]
  public void SiteProgress_UpdatesHpAndKeepsTheBuildersWorking()
  {
    var (world, site, _) = ConstructionWorld();
    var context = new TickContext(world);

    Phase(Responding(
        buildings: [new BuildingProgress(site, SiteHpAfterProgress, IsCompleted: false)],
        events: [new BuildingProgressChanged(site, SiteHpAfterProgress)])).Execute(context);

    BuildingEntity after = context.World.Buildings[site];
    Assert.Equal(SiteHpAfterProgress, after.Hp);
    Assert.False(after.IsCompleted);
    Assert.Equal(UnitOrderKind.Build, context.World.Units[TestWorld.P1WorkerA].Order.Kind);
    Assert.Equal([new BuildingProgressChanged(site, SiteHpAfterProgress)], context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void FarmCompletedInTickN_OperatesFromNPlusOneProducingAndReleasesAllItsBuilders()
  {
    var (world, site, _) = ConstructionWorld();
    var context = new TickContext(world);
    TickId nextTick = context.Tick.Next();

    Phase(Responding(
        buildings: [new BuildingProgress(site, FakeConstructionRules.FarmMaxHp, IsCompleted: true)],
        events: [new BuildingCompleted(site)])).Execute(context);

    BuildingEntity farm = context.World.Buildings[site];
    Assert.Equal(nextTick, farm.OperationalFromTick);
    Assert.Equal(
        new FarmEconomyState(site, PlayerId.One, FarmProductionStatus.Producing, TestWorld.FarmBatches, 0, nextTick),
        farm.FarmEconomy);
    Assert.True(context.World.Units[TestWorld.P1WorkerA].Order.IsIdle);
    Assert.True(context.World.Units[TestWorld.P1WorkerB].Order.IsIdle); // También el que no había llegado.
    Assert.Equal(
        [
          new BuildingCompleted(site),
          new UnitOrderChanged(TestWorld.P1WorkerA, OrderId.None, UnitOrderKind.Idle),
          new UnitOrderChanged(TestWorld.P1WorkerB, OrderId.None, UnitOrderKind.Idle),
        ],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void TowerAndBarracksCompleted_GetTheirCooldownAndAnEmptyProducerFromNPlusOne()
  {
    WorldState world = TestWorld.Create();
    (world, EntityId tower) = TestWorld.WithSite(world, PlayerId.One, BuildingType.Tower, TowerAnchor, SiteHp);
    (world, EntityId barracks) = TestWorld.WithSite(world, PlayerId.One, BuildingType.Barracks, BarracksAnchor, SiteHp);
    var context = new TickContext(world);
    TickId nextTick = context.Tick.Next();

    Phase(Responding(buildings:
    [
      new BuildingProgress(tower, FakeConstructionRules.TowerMaxHp, IsCompleted: true),
      new BuildingProgress(barracks, FakeConstructionRules.BarracksMaxHp, IsCompleted: true),
    ])).Execute(context);

    Assert.Equal(nextTick, context.World.Buildings[tower].TowerReadyAtTick);
    BuildingEntity producer = context.World.Buildings[barracks];
    Assert.Equal(nextTick, producer.OperationalFromTick);
    Assert.NotNull(producer.Production);
  }

  [Fact]
  public void RepairCompleted_FarmProducesAgainFromNPlusOneWithFullBatchesAndSameHp()
  {
    var (world, _, repairing) = ConstructionWorld();
    var context = new TickContext(world);
    TickId nextTick = context.Tick.Next();

    Phase(Responding(
        repairs: [new RepairProgress(repairing, RepairWorkDone, IsCompleted: true)],
        events: [new FarmRepairCompleted(repairing)])).Execute(context);

    BuildingEntity farm = context.World.Buildings[repairing];
    Assert.Null(farm.Repair);
    Assert.Equal(
        new FarmEconomyState(repairing, PlayerId.Two, FarmProductionStatus.Producing, TestWorld.FarmBatches, 0, nextTick),
        farm.FarmEconomy);
    Assert.Equal(TestWorld.FarmHp, farm.Hp);
    Assert.True(context.World.Units[TestWorld.P2Worker].Order.IsIdle);
  }

  [Fact]
  public void RepairProgress_KeepsTheRepairWithTheNewWork()
  {
    var (world, _, repairing) = ConstructionWorld();
    var context = new TickContext(world);

    Phase(Responding(repairs: [new RepairProgress(repairing, RepairWorkAfterProgress, IsCompleted: false)])).Execute(context);

    Assert.Equal(new FarmRepairState(repairing, PlayerId.Two, RepairWorkAfterProgress), context.World.Buildings[repairing].Repair);
    Assert.Equal(UnitOrderKind.RepairFarm, context.World.Units[TestWorld.P2Worker].Order.Kind);
  }

  [Fact]
  public void IncoherentResult_Throws()
  {
    var (world, site, _) = ConstructionWorld();

    Assert.Throws<InvalidOperationException>(() => // "Terminada" sin llegar al HP máximo.
        Phase(Responding(buildings: [new BuildingProgress(site, SiteHpAfterProgress, IsCompleted: true)])).Execute(new TickContext(world)));
    Assert.Throws<InvalidOperationException>(() => // Progreso sobre un edificio que no está en obra.
        Phase(Responding(buildings: [new BuildingProgress(TestWorld.P1Center, SiteHpAfterProgress, IsCompleted: false)])).Execute(new TickContext(world)));
  }
}
