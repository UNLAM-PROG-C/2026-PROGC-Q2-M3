using Rts.Economy;
using Rts.Economy.Model;
using Rts.Movement.Model;
using Rts.Shared.Collections;
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

public class GatherWoodCommandHandlerTests
{
  private const int GatherTicksSoFar = 7;

  private static readonly EquatableArray<PositionXZ> SomeRoute = [TestWorld.NextToTree];

  private static CommandEnvelope Gather(PlayerId player, EntityId target, params EntityId[] units) =>
      new(CommandSequence.First, player, new ClientCommandId(1), new GatherWoodCommand(
          target, MapRevision.Initial, [.. units.Select(unit => new UnitRouteCandidate(unit, SomeRoute))]));

  private static CommandEnvelope Gather(params EntityId[] units) => Gather(PlayerId.One, TestWorld.Tree, units);

  private static GatherWoodCommandHandler Handler(FakeEconomyRules? economy = null, FakeMovementRules? movement = null) =>
      new(economy ?? new FakeEconomyRules(), movement ?? new FakeMovementRules());

  private static WorldState WithDepletedTree(WorldState world)
  {
    TreeEntity tree = world.Trees[TestWorld.Tree];
    return world.With(trees: world.Trees.SetItem(tree.Id, tree.WithEconomy(TreeEconomyState.Depleted(tree.Id, TickId.Zero))));
  }

  [Fact]
  public void Handle_WorkerOnAvailableTree_GetsGatherOrderNewOrderIdAndRoute()
  {
    var context = new TickContext(TestWorld.Create());
    OrderId expectedId = context.World.LastOrderId.Next();

    CommandResult result = Handler().Handle(Gather(TestWorld.P1WorkerA), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    UnitEntity worker = context.World.Units[TestWorld.P1WorkerA];
    Assert.Equal(MovementOrder.Task(expectedId, UnitOrderKind.GatherWood, TestWorld.Tree), worker.Order);
    Assert.Equal(SomeRoute, worker.Route);
    Assert.Equal(
        new UnitOrderChanged(TestWorld.P1WorkerA, expectedId, UnitOrderKind.GatherWood), Assert.Single(context.Events).Payload);
  }

  [Fact]
  public void Handle_WorkerAlreadyGathering_GetsANewOrderAndItsCounterBackToZero()
  {
    var context = new TickContext(TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, GatherTicksSoFar));
    OrderId previous = context.World.Units[TestWorld.P1WorkerA].Order.OrderId;

    Handler().Handle(Gather(TestWorld.P1WorkerA), context);

    UnitEntity worker = context.World.Units[TestWorld.P1WorkerA];
    Assert.NotEqual(previous, worker.Order.OrderId);
    Assert.Equal(0, worker.GatherTicksAccumulated); // 01 §3.8: al reemplazar la orden se descarta el contador.
  }

  [Fact]
  public void Handle_AsksEconomyAboutTheTreeAndMovementWithItsPoint()
  {
    var economy = new FakeEconomyRules();
    var movement = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    PositionXZ treePosition = context.World.Trees[TestWorld.Tree].Position;

    Handler(economy, movement).Handle(Gather(TestWorld.P1WorkerA), context);

    Assert.Equal(new GatherTargetInfo(TestWorld.Tree, EntityKind.Tree, TreeStatus.Available), Assert.Single(economy.GatherValidations));
    var request = Assert.Single(movement.Requests);
    Assert.Equal(UnitOrderKind.GatherWood, request.Kind);
    Assert.Equal(MovementTarget.AtPoint(TestWorld.Tree, treePosition), request.Target);
  }

  [Fact]
  public void Handle_DepletedTree_RejectsTheWholeCommandAndLeavesTheWorldUntouched()
  {
    var context = new TickContext(WithDepletedTree(TestWorld.Create()));
    WorldState before = context.World;

    CommandResult result = Handler().Handle(Gather(TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.All(result.EntityResults, entity => Assert.Equal(EconomyErrors.TreeDepleted, entity.Error));
    Assert.Same(before, context.World);
    Assert.Empty(context.Events);
  }

  [Theory]
  [InlineData(nameof(TestWorld.P2Center), "Economy.InvalidGatherTarget")]
  [InlineData(nameof(TestWorld.P2Worker), "Economy.InvalidGatherTarget")]
  [InlineData(nameof(TestWorld.Missing), "Common.EntityNotFound")]
  public void Handle_TargetThatIsNotATree_IsRejected(string targetName, string expectedError)
  {
    EntityId target = (EntityId)typeof(TestWorld).GetField(targetName)!.GetValue(null)!;
    var context = new TickContext(TestWorld.Create());

    CommandResult result = Handler().Handle(Gather(PlayerId.One, target, TestWorld.P1WorkerA), context);

    Assert.Equal(new ErrorCode(expectedError), Assert.Single(result.EntityResults).Error);
  }

  [Fact]
  public void Handle_RivalWorker_IsRejectedByOwnershipBeforeAskingEconomy()
  {
    var economy = new FakeEconomyRules();
    var context = new TickContext(TestWorld.Create());

    CommandResult result = Handler(economy).Handle(Gather(TestWorld.P2Worker), context);

    Assert.Equal(CommonErrors.NotOwner, Assert.Single(result.EntityResults).Error);
    Assert.Empty(economy.GatherValidations);
  }

  [Fact]
  public void Handle_MovementRejectsOneWorker_PartialResultAndThatWorkerKeepsItsOrder()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerB] = CommonErrors.IncompatibleEntity;
    var context = new TickContext(TestWorld.WithMovingUnit(TestWorld.Create(), TestWorld.P1WorkerB));
    MovementOrder previous = context.World.Units[TestWorld.P1WorkerB].Order;

    CommandResult result = Handler(movement: movement).Handle(Gather(TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.Equal(previous, context.World.Units[TestWorld.P1WorkerB].Order);
    Assert.Equal(UnitOrderKind.GatherWood, context.World.Units[TestWorld.P1WorkerA].Order.Kind);
  }

  [Fact]
  public void Handle_EconomyAcceptsAMissingTarget_IsACriticalFailure()
  {
    var context = new TickContext(TestWorld.Create());
    var careless = new FakeEconomyRules { AcceptEveryGatherTarget = true };

    Assert.Throws<InvalidOperationException>(
        () => Handler(careless).Handle(Gather(PlayerId.One, TestWorld.Missing, TestWorld.P1WorkerA), context));
  }
}
