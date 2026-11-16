using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Simulation.Commands;

/// <summary>
/// Cola global de comandos (contrato 06 §7, D-045). Los receptores concurrentes de Networking admiten por
/// <see cref="ICommandSink"/>; el hilo de Simulation consume por <see cref="ICommandSource"/>.
/// <list type="bullet">
/// <item>Asignar la <see cref="CommandSequence"/> y encolar ocurren dentro del mismo <c>lock</c>: el orden de las Sequence es
/// exactamente el orden de la cola (06 §4).</item>
/// <item>Capacidad finita: con la cola llena se rechaza el comando nuevo con <c>Common.CommandQueueFull</c> y no se descarta
/// nada de lo ya admitido. Un rechazo no consume Sequence.</item>
/// <item>No detecta duplicados: eso lo hace Simulation con su registro de idempotencia (D-043).</item>
/// </list>
/// </summary>
public sealed class CommandQueue : ICommandSink, ICommandSource
{
  private readonly object _gate = new();
  private readonly Queue<CommandEnvelope> _pending = new();
  private readonly int _capacity;
  private CommandSequence _nextSequence = CommandSequence.First;

  public CommandQueue(int capacity)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
    _capacity = capacity;
  }

  /// <summary>Comandos admitidos que todavía no tomó Simulation (diagnóstico y pruebas).</summary>
  public int PendingCount
  {
    get
    {
      lock (_gate)
      {
        return _pending.Count;
      }
    }
  }

  /// <inheritdoc />
  public CommandAdmission Admit(PlayerId player, ClientCommandId clientCommandId, IGameCommand command)
  {
    ArgumentNullException.ThrowIfNull(command);
    if (!player.IsValid)
    {
      throw new ArgumentException($"Jugador inválido: {player}.", nameof(player));
    }

    lock (_gate)
    {
      if (_pending.Count >= _capacity)
      {
        return CommandAdmission.Rejected(CommonErrors.CommandQueueFull);
      }

      CommandSequence sequence = _nextSequence;
      _pending.Enqueue(new CommandEnvelope(sequence, player, clientCommandId, command));
      _nextSequence = sequence.Next();
      return CommandAdmission.Admitted(sequence);
    }
  }

  /// <inheritdoc />
  public IReadOnlyList<CommandEnvelope> DequeueBatch(int maxCount)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
    lock (_gate)
    {
      int batchSize = Math.Min(maxCount, _pending.Count);
      var batch = new CommandEnvelope[batchSize];
      for (int i = 0; i < batchSize; i++)
      {
        batch[i] = _pending.Dequeue();
      }

      return batch;
    }
  }
}
