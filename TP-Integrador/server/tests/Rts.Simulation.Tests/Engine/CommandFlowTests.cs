using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Time;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Engine;

/// <summary>De punta a punta dentro de Simulation: cola → motor → fase 1 → handler → publicación.</summary>
public class CommandFlowTests
{
  private const int QueueCapacity = 100;
  private const int MaxCommandsPerTick = 30;
  private static readonly ClientCommandId MoveId = new(1);
  private static readonly ClientCommandId StopId = new(2);

  [Fact]
  public void MoveThenStop_ArePublishedInOrderAndAResentStopOnlyRepliesTheResult()
  {
    var queue = new CommandQueue(QueueCapacity);
    var handlers = new ICommandHandler[] { new MoveCommandHandler(new FakeMovementRules()), new StopCommandHandler() };
    var phases = new ITickPhase[] { new CommandBatchPhase(queue, MaxCommandsPerTick, handlers) };
    var engine = new SimulationEngine(TestWorld.Factory(), phases, TickRate.Default, TimeProvider.System);
    engine.InitializeMatch();
    var move = new MoveCommand(
        TestWorld.SomeDestination, MapRevision.Initial, [new UnitRouteCandidate(TestWorld.P1WorkerA, [TestWorld.SomeDestination])]);
    var stop = new StopCommand([TestWorld.P1WorkerA]);
    OrderId moveOrder = OrderId.None.Next();

    queue.Admit(PlayerId.One, MoveId, move);
    queue.Admit(PlayerId.One, StopId, stop);
    TickOutcome first = engine.AdvanceTick();
    queue.Admit(PlayerId.One, StopId, stop); // El cliente reenvía el mismo Stop (mismo messageId).
    TickOutcome second = engine.AdvanceTick();

    Assert.Equal(
        [
          new UnitOrderChanged(TestWorld.P1WorkerA, moveOrder, UnitOrderKind.Move),
          new UnitOrderChanged(TestWorld.P1WorkerA, OrderId.None, UnitOrderKind.Idle),
        ],
        first.Publication!.Events.Select(stamped => stamped.Payload));
    Assert.All(first.Publication.CommandResults, result => Assert.Equal(CommandOutcome.Accepted, result.Outcome));
    Assert.True(engine.Current!.Units[TestWorld.P1WorkerA].Order.IsIdle);

    Assert.Empty(second.Publication!.Events);
    Assert.Equal(first.Publication.CommandResults[1], Assert.Single(second.Publication.CommandResults));
    Assert.Equal(1, second.Metrics.DuplicatesReplied);
  }
}
