using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

public class StopCommandHandlerTests
{
  private const int GatherTicksSoFar = 7;

  private static readonly StopCommandHandler Handler = new();

  private static CommandEnvelope Stop(PlayerId player, params EntityId[] units) =>
      new(CommandSequence.First, player, new ClientCommandId(1), new StopCommand([.. units]));

  [Fact]
  public void Handle_MovingUnit_BecomesIdleAndEmitsOrderChanged()
  {
    var context = new TickContext(TestWorld.WithMovingUnit(TestWorld.Create(), TestWorld.P1WorkerA));

    CommandResult result = Handler.Handle(Stop(PlayerId.One, TestWorld.P1WorkerA), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    UnitEntity unit = context.World.Units[TestWorld.P1WorkerA];
    Assert.True(unit.Order.IsIdle);
    Assert.True(unit.Route.IsEmpty);
    DomainEvent changed = Assert.Single(context.Events);
    Assert.Equal(new UnitOrderChanged(TestWorld.P1WorkerA, OrderId.None, UnitOrderKind.Idle), changed.Payload);
  }

  [Fact]
  public void Handle_GatheringUnit_DiscardsGatherCounterButKeepsPositionAndHp()
  {
    WorldState world = TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, GatherTicksSoFar);
    UnitEntity before = world.Units[TestWorld.P1WorkerA];
    var context = new TickContext(world);

    Handler.Handle(Stop(PlayerId.One, TestWorld.P1WorkerA), context);

    UnitEntity after = context.World.Units[TestWorld.P1WorkerA];
    Assert.Equal(0, after.GatherTicksAccumulated);
    Assert.Equal(before.Position, after.Position);
    Assert.Equal(before.Hp, after.Hp);
  }

  [Fact]
  public void Handle_AlreadyIdleUnit_IsAcceptedWithoutEventOrChange()
  {
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;

    CommandResult result = Handler.Handle(Stop(PlayerId.One, TestWorld.P1WorkerA), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.Empty(context.Events);
    Assert.Same(before, context.World);
  }

  [Fact]
  public void Handle_MixedGroup_IsPartiallyAcceptedWithErrorPerUnit()
  {
    WorldState world = TestWorld.WithMovingUnit(TestWorld.Create(), TestWorld.P1WorkerA);
    world = TestWorld.WithMovingUnit(world, TestWorld.P2Worker);
    var context = new TickContext(world);

    CommandResult result = Handler.Handle(Stop(PlayerId.One, TestWorld.P1WorkerA, TestWorld.P2Worker, TestWorld.Missing), context);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.Equal(
        [
          EntityCommandResult.Accepted(TestWorld.P1WorkerA),
          EntityCommandResult.Rejected(TestWorld.P2Worker, CommonErrors.NotOwner),
          EntityCommandResult.Rejected(TestWorld.Missing, CommonErrors.EntityNotFound),
        ],
        result.EntityResults);
    Assert.False(context.World.Units[TestWorld.P2Worker].Order.IsIdle); // La unidad rechazada conserva su orden.
    Assert.Single(context.Events);
  }

  [Fact]
  public void Handle_NoUnitAccepted_IsRejectedAndChangesNothing()
  {
    var context = new TickContext(TestWorld.WithMovingUnit(TestWorld.Create(), TestWorld.P2Worker));
    WorldState before = context.World;

    CommandResult result = Handler.Handle(Stop(PlayerId.One, TestWorld.P2Worker), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(CommonErrors.NoEntityAccepted, result.Error);
    Assert.Same(before, context.World);
    Assert.Empty(context.Events);
  }
}
