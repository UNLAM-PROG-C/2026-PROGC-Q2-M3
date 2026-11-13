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
/// <item><b>Despacho (Strategy):</b> cada comando nuevo va al handler de su tipo. Hay exactamente un handler por
/// <see cref="CommandType"/>, y se exige al construir la fase (D-093): un handler olvidado falla al armar el servidor, no en partida.</item>
/// <item>Todo resultado nuevo se registra en el mundo de trabajo, así que también se descarta si el tick falla.</item>
/// </list>
/// </summary>
internal sealed class CommandBatchPhase : ITickPhase
{
  private readonly ICommandSource _source;
  private readonly int _maxCommandsPerTick;
  private readonly IReadOnlyDictionary<CommandType, ICommandHandler> _handlers;

  /// <exception cref="ArgumentException">Si dos handlers declaran el mismo tipo, o si algún <see cref="CommandType"/> no tiene handler.</exception>
  public CommandBatchPhase(ICommandSource source, int maxCommandsPerTick, IEnumerable<ICommandHandler> handlers)
  {
    ArgumentNullException.ThrowIfNull(handlers);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCommandsPerTick);
    _source = source ?? throw new ArgumentNullException(nameof(source));
    _maxCommandsPerTick = maxCommandsPerTick;
    _handlers = handlers.ToDictionary(handler => handler.Type); // Lanza si dos handlers declaran el mismo tipo.
    EnsureEveryTypeHasAHandler(_handlers);
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

    CommandResult result = _handlers[command.Type].Handle(command, context);
    context.World = context.World.With(idempotency: context.World.Idempotency.Record(result));
    context.AddCommandResult(result);
    Count(result, context.Commands);
  }

  /// <summary>Reemplaza el rechazo temporal <c>Simulation.UnsupportedCommand</c> (D-090) por un error al armar la fase (D-093).</summary>
  private static void EnsureEveryTypeHasAHandler(IReadOnlyDictionary<CommandType, ICommandHandler> handlers)
  {
    CommandType[] missing = [.. Enum.GetValues<CommandType>().Where(type => !handlers.ContainsKey(type))];
    if (missing.Length > 0)
    {
      throw new ArgumentException($"Faltan handlers para: {string.Join(", ", missing)}.", nameof(handlers));
    }
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
