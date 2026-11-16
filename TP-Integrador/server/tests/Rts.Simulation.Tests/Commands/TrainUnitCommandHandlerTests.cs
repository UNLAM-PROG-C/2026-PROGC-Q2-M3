using Rts.Production;
using Rts.Production.Model;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

public class TrainUnitCommandHandlerTests
{
  private const int SiteHp = 10;
  private const int RemainingTrainingTicks = 4;

  private static readonly GridCell FreeCell = new(20, 20);
  private static readonly CommandSequence SecondSequence = new(2);

  private static CommandEnvelope Train(EntityId producer, CommandSequence? sequence = null) =>
      new(sequence ?? CommandSequence.First, PlayerId.One, new ClientCommandId(1), new TrainUnitCommand(producer, UnitType.Worker));

  /// <summary>El Centro Principal de P1 con un trabajador ya entrenándose (tarea activa, cola vacía).</summary>
  private static WorldState WithWorkerInTraining(WorldState world)
  {
    BuildingEntity center = world.Buildings[TestWorld.P1Center];
    ProducerState idle = center.Production!;
    var request = new ProductionRequest(CommandSequence.First, center.Id, PlayerId.One, UnitType.Worker);
    var job = new ProductionJob(request, FakeEconomyRules.WorkerCost.Food, TickId.Zero, RemainingTrainingTicks);
    var training = new ProducerState(idle.ProducerId, idle.Owner, idle.ProducerType, idle.Footprint, idle.OperationalFromTick, job, []);
    return world.With(buildings: world.Buildings.SetItem(center.Id, center.WithProduction(training)));
  }

  [Fact]
  public void Handle_Admitted_QueuesTheRequestWithTheCommandSequenceAndEmitsProductionChanged()
  {
    var context = new TickContext(TestWorld.Create());

    CommandResult result = new TrainUnitCommandHandler(new FakeProductionRules()).Handle(Train(TestWorld.P1Center), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.True(result.EntityResults.IsEmpty); // No es un comando grupal.
    ProducerState producer = context.World.Buildings[TestWorld.P1Center].Production!;
    ProductionRequest queued = Assert.Single(producer.Pending);
    Assert.Equal(new ProductionRequest(CommandSequence.First, TestWorld.P1Center, PlayerId.One, UnitType.Worker), queued);
    Assert.Null(producer.ActiveJob);
    DomainEvent changed = Assert.Single(context.Events);
    Assert.Equal(
        new ProductionChanged(TestWorld.P1Center, PlayerId.One, ProducerStatus.WaitingForResources, null, null, [UnitType.Worker]),
        changed.Payload);
  }

  [Fact]
  public void Handle_DoesNotChargeFood()
  {
    var context = new TickContext(TestWorld.Create());
    var before = context.World.Players;

    new TrainUnitCommandHandler(new FakeProductionRules()).Handle(Train(TestWorld.P1Center), context);

    Assert.Equal(before, context.World.Players); // La comida se cobra al iniciar, en la fase 8 (05 §2).
  }

  [Fact]
  public void Handle_PassesTheProducerDataToProduction()
  {
    var rules = new FakeProductionRules();
    var context = new TickContext(TestWorld.Create());

    new TrainUnitCommandHandler(rules).Handle(Train(TestWorld.P1Center), context);

    TrainRequestCall call = Assert.Single(rules.TrainRequests);
    Assert.Equal(PlayerId.One, call.Requester);
    Assert.Equal(UnitType.Worker, call.UnitType);
    Assert.Equal(
        new ProducerInfo(TestWorld.P1Center, PlayerId.One, BuildingType.MainCenter, IsOperational: true, PendingCount: 0),
        call.Producer);
  }

  [Fact]
  public void Handle_SecondRequestInTheSameBatch_SeesTheFirstInTheQueue()
  {
    var rules = new FakeProductionRules();
    var context = new TickContext(TestWorld.Create());
    var handler = new TrainUnitCommandHandler(rules);

    handler.Handle(Train(TestWorld.P1Center), context);
    handler.Handle(Train(TestWorld.P1Center, SecondSequence), context);

    Assert.Equal(1, rules.TrainRequests[1].Producer!.PendingCount);
    Assert.Equal(
        [CommandSequence.First, SecondSequence],
        context.World.Buildings[TestWorld.P1Center].Production!.Pending.Select(request => request.Sequence));
  }

  [Fact]
  public void Handle_ProducerAlreadyTraining_KeepsTheActiveJobAndReportsIt()
  {
    var context = new TickContext(WithWorkerInTraining(TestWorld.Create()));

    new TrainUnitCommandHandler(new FakeProductionRules()).Handle(Train(TestWorld.P1Center, SecondSequence), context);

    ProducerState producer = context.World.Buildings[TestWorld.P1Center].Production!;
    Assert.Equal(RemainingTrainingTicks, producer.ActiveJob!.RemainingTicks);
    Assert.Equal(
        new ProductionChanged(
            TestWorld.P1Center, PlayerId.One, ProducerStatus.Training, UnitType.Worker, RemainingTrainingTicks, [UnitType.Worker]),
        Assert.Single(context.Events).Payload);
  }

  [Fact]
  public void Handle_BarracksUnderConstruction_IsReportedAsNotOperational()
  {
    var rules = new FakeProductionRules { Rejection = ProductionErrors.ProducerNotOperational };
    (WorldState world, EntityId siteId) = TestWorld.WithSite(TestWorld.Create(), PlayerId.One, BuildingType.Barracks, FreeCell, SiteHp);
    var context = new TickContext(world);

    new TrainUnitCommandHandler(rules).Handle(Train(siteId), context);

    Assert.Equal(
        new ProducerInfo(siteId, PlayerId.One, BuildingType.Barracks, IsOperational: false, PendingCount: 0),
        Assert.Single(rules.TrainRequests).Producer);
  }

  [Fact]
  public void Handle_MissingProducer_SendsNullToProduction()
  {
    var rules = new FakeProductionRules { Rejection = CommonErrors.EntityNotFound };
    var context = new TickContext(TestWorld.Create());

    CommandResult result = new TrainUnitCommandHandler(rules).Handle(Train(TestWorld.Missing), context);

    Assert.Null(Assert.Single(rules.TrainRequests).Producer);
    Assert.Equal(CommonErrors.EntityNotFound, result.Error);
  }

  [Fact]
  public void Handle_RejectedByProduction_LeavesTheWorldUntouched()
  {
    var rules = new FakeProductionRules { Rejection = ProductionErrors.QueueFull };
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;

    CommandResult result = new TrainUnitCommandHandler(rules).Handle(Train(TestWorld.P1Center), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(ProductionErrors.QueueFull, result.Error);
    Assert.Same(before, context.World);
    Assert.Empty(context.Events);
  }

  [Fact]
  public void Handle_ProductionAdmitsSomethingThatIsNotAProducer_IsACriticalFailure()
  {
    (WorldState world, EntityId siteId) = TestWorld.WithSite(TestWorld.Create(), PlayerId.One, BuildingType.Barracks, FreeCell, SiteHp);
    var context = new TickContext(world);

    Assert.Throws<InvalidOperationException>(
        () => new TrainUnitCommandHandler(new FakeProductionRules()).Handle(Train(siteId), context));
  }

  [Fact]
  public void Handle_ProductionAdmitsARivalProducer_IsACriticalFailure()
  {
    var context = new TickContext(TestWorld.Create());

    Assert.ThrowsAny<ArgumentException>(
        () => new TrainUnitCommandHandler(new FakeProductionRules()).Handle(Train(TestWorld.P2Center), context));
  }
}
