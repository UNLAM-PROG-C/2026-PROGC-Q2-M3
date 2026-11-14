using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using static Rts.Production.Tests.Fixtures;

namespace Rts.Production.Tests;

/// <summary>
/// Inicio de entrenamientos dentro de un tick: cobro de comida, prioridad estricta por <c>Sequence</c> y quién puede actuar
/// (contrato 05 §4, D-037 y D-038).
/// </summary>
public class ProductionStartTests
{
  private const long Tick = 10;

  private static readonly ProductionCalculator Calculator = Fixtures.Calculator();

  private static ProductionTickResult Run(ProducerState[] producers, int foodP1 = PlentyOfFood, int foodP2 = PlentyOfFood, long tick = Tick) =>
      Calculator.CalculateTick(Input(tick, producers, foodP1, foodP2));

  private static ProductionStart StartOf(ProductionRequest request, int paid) =>
      new(request.ProducerId, request.Owner, request.Sequence, request.UnitType, paid);

  [Fact]
  public void TheOldestRequest_Starts_ChargingItsCost_AndReportsTheTaskAndTheEvent()
  {
    ProductionRequest request = WorkerRequest(1, P1);

    ProductionTickResult result = Run([Center(P1, request)]);

    Assert.Equal<ProductionStart>([StartOf(request, WorkerCost)], result.Starts);
    ProducerState updated = Assert.Single(result.ProducerUpdates);
    Assert.Equal(new ProductionJob(request, WorkerCost, new TickId(Tick), WorkerDuration), updated.ActiveJob);
    Assert.Empty(updated.Pending);
    Assert.Equal<IDomainEventPayload>(
        [new ProductionChanged(CenterId(P1), P1, ProducerStatus.Training, UnitType.Worker, WorkerDuration, [])], result.ProposedEvents);
  }

  [Fact]
  public void AUnitUsesItsOwnTrainingDuration()
  {
    ProductionTickResult result = Run([Barracks(P1, SoldierRequest(1, P1))]);

    Assert.Equal(SoldierDuration, Assert.Single(result.ProducerUpdates).ActiveJob!.RemainingTicks);
    Assert.Equal(SoldierCost, Assert.Single(result.Starts).PaidFood);
  }

  [Fact]
  public void WithoutEnoughFood_NothingStarts_AndTheRequestKeepsWaiting()
  {
    ProductionTickResult result = Run([Center(P1, WorkerRequest(1, P1))], foodP1: WorkerCost - 1);

    Assert.Equal(ProductionTickResult.Empty(new TickId(Tick)), result);
  }

  [Fact]
  public void WithExactlyTheCost_ItStarts()
  {
    ProductionTickResult result = Run([Center(P1, WorkerRequest(1, P1))], foodP1: WorkerCost);

    Assert.Single(result.Starts);
  }

  [Fact]
  public void AUnitThatCostsNothing_StartsEvenWithoutFood()
  {
    ProductionCalculator free = Fixtures.Calculator(workerCost: 0);

    ProductionTickResult result = free.CalculateTick(Input(Tick, [Center(P1, WorkerRequest(1, P1))], foodP1: 0));

    Assert.Equal(0, Assert.Single(result.Starts).PaidFood);
  }

  [Fact]
  public void OnlyTheHeadOfTheQueueStarts_TheRestStayInOrder()
  {
    ProductionRequest first = WorkerRequest(1, P1);
    ProductionRequest second = WorkerRequest(2, P1);

    ProductionTickResult result = Run([Center(P1, first, second)]);

    Assert.Equal(first.Sequence, Assert.Single(result.Starts).RequestSequence);
    Assert.Equal<ProductionRequest>([second], Assert.Single(result.ProducerUpdates).Pending);
    ProductionChanged changed = Assert.IsType<ProductionChanged>(Assert.Single(result.ProposedEvents));
    Assert.Equal<UnitType>([UnitType.Worker], changed.PendingUnitTypes);
  }

  [Fact]
  public void StrictPriority_AnOlderRequestThePlayerCannotAfford_BlocksTheCheaperLaterOnes()
  {
    // El soldado (costo 5) es más viejo que el trabajador (costo 3). Con 4 de comida alcanzaría para el trabajador, pero no se
    // salta al soldado: la solicitud más antigua nunca queda postergada.
    ProducerState[] producers = [Barracks(P1, SoldierRequest(1, P1)), Center(P1, WorkerRequest(2, P1))];

    ProductionTickResult result = Run(producers, foodP1: 4);

    Assert.Equal(ProductionTickResult.Empty(new TickId(Tick)), result);
  }

  [Fact]
  public void TheOlderAffordableRequestStarts_AndTheLaterOneThatCannotPayWaits()
  {
    ProducerState[] producers = [Center(P1, WorkerRequest(1, P1)), Barracks(P1, SoldierRequest(2, P1))];

    ProductionTickResult result = Run(producers, foodP1: 4);

    Assert.Equal(new CommandSequence(1), Assert.Single(result.Starts).RequestSequence);
  }

  [Fact]
  public void WithFoodForBoth_BothStart_InSequenceOrder()
  {
    ProducerState[] producers = [Barracks(P1, SoldierRequest(2, P1)), Center(P1, WorkerRequest(1, P1))];

    ProductionTickResult result = Run(producers, foodP1: WorkerCost + SoldierCost);

    Assert.Equal<CommandSequence>([new CommandSequence(1), new CommandSequence(2)], result.Starts.Select(start => start.RequestSequence));
  }

  [Theory]
  [InlineData(3, 5, UnitType.Worker)]
  [InlineData(5, 3, UnitType.Soldier)]
  public void TwoProducersCompetingForTheSameFood_ThePlayerPaysTheOldestFirst(long workerSequence, long soldierSequence, UnitType expected)
  {
    ProducerState[] producers = [Center(P1, WorkerRequest(workerSequence, P1)), Barracks(P1, SoldierRequest(soldierSequence, P1))];

    ProductionTickResult result = Run(producers, foodP1: 6);

    Assert.Equal(expected, Assert.Single(result.Starts).UnitType);
  }

  [Fact]
  public void APlayerWhoCannotPay_DoesNotBlockTheOtherPlayer()
  {
    ProducerState[] producers = [Center(P1, WorkerRequest(1, P1)), Center(P2, WorkerRequest(2, P2))];

    ProductionTickResult result = Run(producers, foodP1: 0, foodP2: PlentyOfFood);

    ProductionStart start = Assert.Single(result.Starts);
    Assert.Equal(P2, start.Owner);
  }

  [Fact]
  public void ABuildingThatBecomesOperationalAtN1_DoesNotActAtN_AndStartsAtN1()
  {
    ProducerState[] producers = [OperationalFrom(Barracks(P1, SoldierRequest(1, P1)), Tick + 1)];

    ProductionTickResult atCompletion = Run(producers, tick: Tick);
    ProductionTickResult nextTick = Run(producers, tick: Tick + 1);

    Assert.Equal(ProductionTickResult.Empty(new TickId(Tick)), atCompletion);
    Assert.Single(nextTick.Starts);
  }

  [Fact]
  public void ABusyProducer_DoesNotStartAnotherTask_AndOnlyProgresses()
  {
    ProducerState busy = WithJob(Center(P1), WorkerRequest(1, P1), remaining: 3, WorkerRequest(2, P1));

    ProductionTickResult result = Run([busy]);

    Assert.Empty(result.Starts);
    Assert.Equal(2, Assert.Single(result.ProducerUpdates).ActiveJob!.RemainingTicks);
    Assert.Empty(result.ProposedEvents);
  }

  [Fact]
  public void AProducerThatFinishesAndSpawnsThisTick_DoesNotStartTheNextTaskUntilTheNextTick()
  {
    ProducerState finishing = WithJob(Center(P1), WorkerRequest(1, P1), remaining: 1, WorkerRequest(2, P1));

    ProductionTickResult result = Run([finishing]);

    Assert.Single(result.Spawns);
    Assert.Empty(result.Starts);
    Assert.Equal(ProducerStatus.WaitingForResources, Assert.Single(result.ProducerUpdates).Status);
  }

  [Fact]
  public void Constructor_RejectsMissingNegativeOrNullCosts()
  {
    var onlyWorker = new Dictionary<UnitType, int> { [UnitType.Worker] = 1 };
    var negative = new Dictionary<UnitType, int> { [UnitType.Worker] = 1, [UnitType.Soldier] = -1 };

    Assert.Throws<ArgumentException>(() => new ProductionCalculator(Rules(), onlyWorker));
    Assert.Throws<ArgumentException>(() => new ProductionCalculator(Rules(), negative));
    Assert.Throws<ArgumentNullException>(() => new ProductionCalculator(null!, Costs()));
    Assert.Throws<ArgumentNullException>(() => new ProductionCalculator(Rules(), null!));
    Assert.Throws<ArgumentNullException>(() => Calculator.CalculateTick(null!));
  }
}
