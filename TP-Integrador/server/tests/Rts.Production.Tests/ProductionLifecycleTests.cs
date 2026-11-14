using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using static Rts.Production.Tests.Fixtures;

namespace Rts.Production.Tests;

/// <summary>
/// El ciclo de vida de una unidad a lo largo de varios ticks: duración, cobro único, aparición, aparición pendiente y determinismo
/// (contrato 05 §4, D-037, D-039, D-041 y D-042). Un <see cref="ProductionSim"/> aplica cada resultado como lo haría Simulation.
/// </summary>
public class ProductionLifecycleTests
{
  private const long StartTick = 10;
  private const int StartingFood = 10;

  private static readonly ProductionCalculator Calculator = Fixtures.Calculator();

  private static ProductionSim NewSim(ProducerState[] producers, int food = StartingFood, int radius = DefaultRadius) =>
      new(Fixtures.Calculator(radius), StartTick, producers, food);

  private static SpawnProposal WorkerAt(GridCell cell, long sequence = 1) =>
      new(CenterId(P1), P1, new CommandSequence(sequence), UnitType.Worker, cell, new PositionXZ(cell.Column + 0.5, cell.Row + 0.5));

  [Fact]
  public void AUnitStartedAtN_WithDurationD_AppearsExactlyAtNPlusD()
  {
    ProductionSim sim = NewSim([Center(P1, WorkerRequest(1, P1))]);

    ProductionTickResult started = sim.Step();
    List<ProductionTickResult> training = sim.Steps(WorkerDuration - 1);
    ProductionTickResult appearance = sim.Step();

    Assert.Single(started.Starts);
    Assert.All(training, result => Assert.Empty(result.Spawns));
    Assert.Equal(new TickId(StartTick + WorkerDuration), appearance.Tick);
    Assert.Equal(WorkerAt(new GridCell(5, 3)), Assert.Single(appearance.Spawns));
  }

  [Fact]
  public void TheFoodIsChargedExactlyOnce_ForTheWholeLifeOfTheUnit()
  {
    ProductionSim sim = NewSim([Center(P1, WorkerRequest(1, P1))]);

    List<ProductionTickResult> results = sim.Steps(WorkerDuration + 3);

    Assert.Equal(WorkerCost, results.SelectMany(result => result.Starts).Sum(start => start.PaidFood));
    Assert.Equal(StartingFood - WorkerCost, sim.Food[P1]);
  }

  [Fact]
  public void ProgressTicks_UpdateTheProducer_ButPublishNoEvent()
  {
    ProductionSim sim = NewSim([Center(P1, WorkerRequest(1, P1))]);
    sim.Step();

    ProductionTickResult progress = sim.Step();

    Assert.Equal(WorkerDuration - 1, Assert.Single(progress.ProducerUpdates).ActiveJob!.RemainingTicks);
    Assert.Empty(progress.ProposedEvents);
  }

  [Fact]
  public void TheNextQueuedTask_StartsOnlyTheTickAfterTheSpawn()
  {
    ProductionSim sim = NewSim([Center(P1, WorkerRequest(1, P1), WorkerRequest(2, P1))]);
    sim.Steps(WorkerDuration);

    ProductionTickResult spawnTick = sim.Step();
    ProductionTickResult nextTick = sim.Step();

    Assert.Single(spawnTick.Spawns);
    Assert.Empty(spawnTick.Starts);
    Assert.Equal(
        new ProductionChanged(CenterId(P1), P1, ProducerStatus.WaitingForResources, null, null, [UnitType.Worker]),
        Assert.Single(spawnTick.ProposedEvents));
    Assert.Equal(new CommandSequence(2), Assert.Single(nextTick.Starts).RequestSequence);
  }

  [Fact]
  public void WithNoFreeCell_TheUnitWaitsInPendingSpawn_AndIsNotChargedAgain()
  {
    ProductionSim sim = NewSim([Center(P1, WorkerRequest(1, P1))]);
    sim.View = ViewWithStructures(CellsAround(CenterFootprint(P1), 1, DefaultRadius));
    sim.Steps(WorkerDuration);

    ProductionTickResult blocked = sim.Step();
    ProductionTickResult stillBlocked = sim.Step();
    sim.View = EmptyView;
    ProductionTickResult freed = sim.Step();

    Assert.Empty(blocked.Spawns);
    Assert.Equal(
        new ProductionChanged(CenterId(P1), P1, ProducerStatus.PendingSpawn, UnitType.Worker, 0, []),
        Assert.Single(blocked.ProposedEvents));
    Assert.Equal(ProductionTickResult.Empty(stillBlocked.Tick), stillBlocked);
    Assert.Single(freed.Spawns);
    Assert.Empty(freed.Starts);
    Assert.Equal(StartingFood - WorkerCost, sim.Food[P1]);
  }

  [Fact]
  public void AProducerWaitingToSpawn_DoesNotStartTheNextTaskInTheTickItFinallySpawns()
  {
    ProductionSim sim = NewSim([Center(P1, WorkerRequest(1, P1), WorkerRequest(2, P1))]);
    sim.View = ViewWithStructures(CellsAround(CenterFootprint(P1), 1, DefaultRadius));
    sim.Steps(WorkerDuration + 1);
    sim.View = EmptyView;

    ProductionTickResult freed = sim.Step();
    ProductionTickResult after = sim.Step();

    Assert.Single(freed.Spawns);
    Assert.Empty(freed.Starts);
    Assert.Single(after.Starts);
  }

  [Fact]
  public void TwoSimultaneousSpawns_NeverShareACell_AndTheOlderRequestGetsTheBetterOne()
  {
    // El Cuartel (Sequence 1) y el Centro (Sequence 2) comparten solo dos celdas libres: (8,5) y (8,6). Las dos están a la misma
    // distancia de ambos centros, así que la más vieja se queda con (8,5) y la otra con (8,6), sin importar el ProducerId.
    var wideBarracks = new Footprint(new GridCell(9, 4), new FootprintSize(4, 4));
    ProductionRequest soldier = SoldierRequest(1, P1);
    ProductionRequest worker = WorkerRequest(2, P1);
    ProducerState barracks = new(BarracksId(P1), P1, BuildingType.Barracks, wideBarracks, TickId.Zero, new ProductionJob(soldier, SoldierCost, TickId.Zero, 0), default);
    ProducerState center = WithJob(Center(P1), worker, remaining: 0);
    IEnumerable<GridCell> ringOnes = CellsAround(CenterFootprint(P1), 1, 1).Concat(CellsAround(wideBarracks, 1, 1)).Distinct();
    SpawnView view = ViewWithStructures(ringOnes.Where(cell => cell != new GridCell(8, 5) && cell != new GridCell(8, 6)));

    ProductionTickResult result = Fixtures.Calculator(radius: 1).CalculateTick(Input(StartTick, [center, barracks], view: view));

    Assert.Equal<GridCell>([new GridCell(8, 5), new GridCell(8, 6)], result.Spawns.Select(spawn => spawn.Cell));
    Assert.Equal<UnitType>([UnitType.Soldier, UnitType.Worker], result.Spawns.Select(spawn => spawn.UnitType));
  }

  [Fact]
  public void WhenOnlyOneCellIsFreeForTwoSpawns_TheOldestGetsItAndTheOtherKeepsWaiting()
  {
    var wideBarracks = new Footprint(new GridCell(9, 4), new FootprintSize(4, 4));
    ProducerState barracks = new(BarracksId(P1), P1, BuildingType.Barracks, wideBarracks, TickId.Zero, new ProductionJob(SoldierRequest(1, P1), SoldierCost, TickId.Zero, 0), default);
    ProducerState center = WithJob(Center(P1), WorkerRequest(2, P1), remaining: 0);
    IEnumerable<GridCell> ringOnes = CellsAround(CenterFootprint(P1), 1, 1).Concat(CellsAround(wideBarracks, 1, 1)).Distinct();
    SpawnView view = ViewWithStructures(ringOnes.Where(cell => cell != new GridCell(8, 5)));

    ProductionTickResult result = Fixtures.Calculator(radius: 1).CalculateTick(Input(StartTick, [center, barracks], view: view));

    SpawnProposal only = Assert.Single(result.Spawns);
    Assert.Equal(UnitType.Soldier, only.UnitType);
    Assert.Equal(new GridCell(8, 5), only.Cell);
  }

  [Fact]
  public void TheResult_DoesNotDependOnTheOrderOfTheInputs()
  {
    ProducerState[] producers =
    [
      Center(P1, WorkerRequest(1, P1), WorkerRequest(5, P1)),
      WithJob(Barracks(P1), SoldierRequest(2, P1), remaining: 0),
      WithJob(Center(P2), WorkerRequest(3, P2), remaining: 3, WorkerRequest(6, P2)),
      Barracks(P2, SoldierRequest(4, P2)),
    ];
    ProductionTickResult expected = Calculator.CalculateTick(Input(StartTick, producers, foodP2: SoldierCost * 2));

    ProducerState[] reversed = [.. producers.Reverse()];
    ProducerState[] rotated = [.. producers.Skip(1), .. producers.Take(1)];

    Assert.Equal(expected, Calculator.CalculateTick(Input(StartTick, reversed, foodP2: SoldierCost * 2)));
    Assert.Equal(expected, Calculator.CalculateTick(Input(StartTick, rotated, foodP2: SoldierCost * 2)));
    Assert.Equal(expected, Calculator.CalculateTick(ReversedFood(StartTick, producers, SoldierCost * 2)));
  }

  [Fact]
  public void TheScenarioUsedForDeterminism_ReallyExercisesEveryPartOfThePhase()
  {
    ProducerState[] producers =
    [
      Center(P1, WorkerRequest(1, P1), WorkerRequest(5, P1)),
      WithJob(Barracks(P1), SoldierRequest(2, P1), remaining: 0),
      WithJob(Center(P2), WorkerRequest(3, P2), remaining: 3, WorkerRequest(6, P2)),
      Barracks(P2, SoldierRequest(4, P2)),
    ];

    ProductionTickResult result = Calculator.CalculateTick(Input(StartTick, producers, foodP2: SoldierCost * 2));

    Assert.Equal(2, result.Starts.Count);
    Assert.Single(result.Spawns);
    Assert.Equal(4, result.ProducerUpdates.Count);
    Assert.Equal(3, result.ProposedEvents.Count);
  }

  [Fact]
  public void TheSameInputTwice_GivesTheSameResult()
  {
    ProductionTickInput input = Input(StartTick, [Center(P1, WorkerRequest(1, P1)), Barracks(P2, SoldierRequest(2, P2))]);

    Assert.Equal(Calculator.CalculateTick(input), Calculator.CalculateTick(input));
  }

  private static ProductionTickInput ReversedFood(long tick, ProducerState[] producers, int foodP2) =>
      new(new TickId(tick), [.. producers], [new PlayerFood(P2, foodP2), new PlayerFood(P1, PlentyOfFood)], EmptyView);
}
