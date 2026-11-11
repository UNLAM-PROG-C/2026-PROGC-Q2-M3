using Rts.Shared.Commands;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;

namespace Rts.Simulation.Commands;

/// <summary>
/// Fase 1 del tick (contrato 06 §3-4): toma un lote acotado de la cola global y procesa cada comando en orden de Sequence.
/// <list type="bullet">
/// <item><b>Idempotencia (D-043):</b> si la clave <c>(PlayerId, ClientCommandId)</c> ya tiene resultado en el registro, el
/// comando no se ejecuta y se vuelve a publicar el resultado original. Como la cola es FIFO, el original siempre se procesa antes
/// que sus duplicados, incluso dentro del mismo lote.</item>
/// <item><b>Despacho (Strategy):</b> cada comando nuevo va al handler de su tipo. Un tipo sin handler se rechaza con
/// <see cref="SimulationErrors.UnsupportedCommand"/>.</item>
/// <item>Todo resultado nuevo se registra en el mundo de trabajo, así que también se descarta si el tick falla.</item>
/// </list>
/// </summary>
internal sealed class CommandBatchPhase : ITickPhase
{
  private readonly ICommandSource _source;
  private readonly int _maxCommandsPerTick;
  private readonly IReadOnlyDictionary<CommandType, ICommandHandler> _handlers;

  public CommandBatchPhase(ICommandSource source, int maxCommandsPerTick, IEnumerable<ICommandHandler> handlers)
  {
    ArgumentNullException.ThrowIfNull(handlers);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCommandsPerTick);
    _source = source ?? throw new ArgumentNullException(nameof(source));
    _maxCommandsPerTick = maxCommandsPerTick;
    _handlers = handlers.ToDictionary(handler => handler.Type); // Lanza si dos handlers declaran el mismo tipo.
  }

  public SimulationPhase Phase => SimulationPhase.CommandBatch;

  public void Execute(TickContext context)
  {
    IReadOnlyList<CommandEnvelope> batch = _source.DequeueBatch(_maxCommandsPerTick);
    context.Commands.Dequeued = batch.Count;
    foreach (CommandEnvelope command in batch)
    {
      Process(command, context);
    }
  }

  private void Process(CommandEnvelope command, TickContext context)
  {
    if (context.World.Idempotency.TryGet(command.Key, out CommandResult? original))
    {
      context.AddCommandResult(original!);
      context.Commands.DuplicatesReplied++;
      return;
    }

    CommandResult result = _handlers.TryGetValue(command.Type, out ICommandHandler? handler)
        ? handler.Handle(command, context)
        : CommandResult.Rejected(command, context.Tick, SimulationErrors.UnsupportedCommand);

    context.World = context.World.With(idempotency: context.World.Idempotency.Record(result));
    context.AddCommandResult(result);
    Count(result, context.Commands);
  }

  private static void Count(CommandResult result, CommandCounters counters)
  {
    if (result.Outcome == CommandOutcome.Rejected)
    {
      counters.Rejected++;
    }
    else
    {
      counters.Accepted++;
    }
  }
}
