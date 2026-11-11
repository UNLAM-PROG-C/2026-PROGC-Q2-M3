using Rts.Shared.Commands;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

public class CommandBatchPhaseTests
{
  private const int QueueCapacity = 100;
  private const int MaxCommandsPerTick = 3;
  private const int CommandsAdmitted = 5;

  private static readonly IGameCommand AnyStop = new StopCommand([TestWorld.P1WorkerA]);
  private static readonly IGameCommand OtherStop = new StopCommand([TestWorld.P1WorkerB]);
  private static readonly IGameCommand AnyMove = new MoveCommand(
      TestWorld.SomeDestination, MapRevision.Initial, [new UnitRouteCandidate(TestWorld.P1WorkerA, [TestWorld.SomeDestination])]);

  private static ClientCommandId ClientId(long value) => new(value);

  private static (CommandQueue Queue, RecordingHandler Handler, CommandBatchPhase Phase) CreatePhase()
  {
    var queue = new CommandQueue(QueueCapacity);
    var handler = new RecordingHandler();
    return (queue, handler, new CommandBatchPhase(queue, MaxCommandsPerTick, [handler]));
  }

  [Fact]
  public void Execute_TakesAtMostTheLimitAndLeavesTheRestQueued()
  {
    var (queue, handler, phase) = CreatePhase();
    for (int i = 1; i <= CommandsAdmitted; i++)
    {
      queue.Admit(PlayerId.One, ClientId(i), AnyStop);
    }

    var context = new TickContext(TestWorld.Create());
    phase.Execute(context);

    Assert.Equal(MaxCommandsPerTick, handler.Calls);
    Assert.Equal(MaxCommandsPerTick, context.Commands.Dequeued);
    Assert.Equal(MaxCommandsPerTick, context.Commands.Accepted);
    Assert.Equal(CommandsAdmitted - MaxCommandsPerTick, queue.PendingCount);
  }

  [Fact]
  public void Execute_DuplicateInSameBatch_RunsOnceAndRepliesOriginalResult()
  {
    var (queue, handler, phase) = CreatePhase();
    queue.Admit(PlayerId.One, ClientId(1), AnyStop);
    queue.Admit(PlayerId.One, ClientId(1), OtherStop); // Misma clave, otro contenido: igual es duplicado (06 §3).

    var context = new TickContext(TestWorld.Create());
    phase.Execute(context);

    Assert.Equal(1, handler.Calls);
    Assert.Equal(1, context.Commands.DuplicatesReplied);
    Assert.Equal(1, context.World.Idempotency.Count);
    Assert.Same(context.CommandResults[0], context.CommandResults[1]);
  }

  [Fact]
  public void Execute_DuplicateInLaterTick_RepliesResultFromOriginalTick()
  {
    var (queue, handler, phase) = CreatePhase();
    queue.Admit(PlayerId.One, ClientId(1), AnyStop);
    var first = new TickContext(TestWorld.Create());
    phase.Execute(first);

    queue.Admit(PlayerId.One, ClientId(1), AnyStop);
    var second = new TickContext(first.World); // Como si el primer tick se hubiera confirmado.
    phase.Execute(second);

    Assert.Equal(1, handler.Calls);
    CommandResult replied = Assert.Single(second.CommandResults);
    Assert.Equal(first.Tick, replied.Tick);
  }

  [Fact]
  public void Execute_SameClientIdFromOtherPlayer_IsNotADuplicate()
  {
    var (queue, handler, phase) = CreatePhase();
    queue.Admit(PlayerId.One, ClientId(1), AnyStop);
    queue.Admit(PlayerId.Two, ClientId(1), AnyStop);

    var context = new TickContext(TestWorld.Create());
    phase.Execute(context);

    Assert.Equal(2, handler.Calls);
    Assert.Equal(0, context.Commands.DuplicatesReplied);
  }

  [Fact]
  public void Execute_TypeWithoutHandler_IsRejectedAndRecorded()
  {
    var (queue, handler, phase) = CreatePhase();
    queue.Admit(PlayerId.One, ClientId(1), AnyMove);

    var context = new TickContext(TestWorld.Create());
    phase.Execute(context);

    CommandResult result = Assert.Single(context.CommandResults);
    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(SimulationErrors.UnsupportedCommand, result.Error);
    Assert.Equal(1, context.Commands.Rejected);
    Assert.Equal(0, handler.Calls);
    Assert.Equal(1, context.World.Idempotency.Count);
  }

  [Fact]
  public void Constructor_TwoHandlersForSameType_Throws()
  {
    Assert.Throws<ArgumentException>(() =>
        new CommandBatchPhase(new CommandQueue(QueueCapacity), MaxCommandsPerTick, [new RecordingHandler(), new RecordingHandler()]));
  }

  /// <summary>Handler falso de Stop: acepta siempre y cuenta las llamadas, sin tocar el mundo.</summary>
  private sealed class RecordingHandler : ICommandHandler
  {
    public int Calls { get; private set; }

    public CommandType Type => CommandType.Stop;

    public CommandResult Handle(CommandEnvelope command, TickContext context)
    {
      Calls++;
      return CommandResult.Accepted(command, context.Tick);
    }
  }
}
