using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Time;
using Rts.Simulation.Commands;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Engine;

/// <summary>De punta a punta dentro de Simulation: cola → motor → fase 1 → handler → publicación.</summary>
public class CommandFlowTests
{
  private const int QueueCapacity = 100;
  private const int MaxCommandsPerTick = 30;
  private static readonly ClientCommandId StopId = new(1);
  private static readonly TickId FirstTick = TickId.Zero.Next();

  [Fact]
  public void StopAdmittedToQueue_IsPublishedOnceAndItsDuplicateOnlyRepliesTheResult()
  {
    var queue = new CommandQueue(QueueCapacity);
    var phases = new ITickPhase[]
    {
      new StartWalkingOnFirstTick(),
      new CommandBatchPhase(queue, MaxCommandsPerTick, [new StopCommandHandler()]),
    };
    var engine = new SimulationEngine(TestWorld.Factory(), phases, TickRate.Default, TimeProvider.System);
    engine.InitializeMatch();
    var stop = new StopCommand([TestWorld.P1WorkerA]);

    queue.Admit(PlayerId.One, StopId, stop);
    TickOutcome first = engine.AdvanceTick();
    queue.Admit(PlayerId.One, StopId, stop); // El cliente reenvía el mismo comando (mismo messageId).
    TickOutcome second = engine.AdvanceTick();

    DomainEvent changed = Assert.Single(first.Publication!.Events);
    Assert.Equal(new UnitOrderChanged(TestWorld.P1WorkerA, OrderId.None, UnitOrderKind.Idle), changed.Payload);
    CommandResult original = Assert.Single(first.Publication.CommandResults);
    Assert.Equal(CommandOutcome.Accepted, original.Outcome);
    Assert.True(engine.Current!.Units[TestWorld.P1WorkerA].Order.IsIdle);

    Assert.Empty(second.Publication!.Events);
    Assert.Equal(original, Assert.Single(second.Publication.CommandResults));
    Assert.Equal(1, second.Metrics.DuplicatesReplied);
  }

  /// <summary>Fase de prueba: en el tick 1, antes de la fase 1, pone a caminar a la unidad (todavía no existe el handler de Move).</summary>
  private sealed class StartWalkingOnFirstTick : ITickPhase
  {
    public SimulationPhase Phase => SimulationPhase.CommandBatch;

    public void Execute(TickContext context)
    {
      if (context.Tick == FirstTick)
      {
        context.World = TestWorld.WithMovingUnit(context.World, TestWorld.P1WorkerA);
      }
    }
  }
}
