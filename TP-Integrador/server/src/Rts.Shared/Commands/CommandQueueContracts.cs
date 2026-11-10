using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Shared.Commands;

/// <summary>
/// Lado productor de la cola global de comandos (06 §4, 08 §4). Lo usan los receptores de Networking, que son
/// concurrentes, ya con el jugador identificado por la conexión y el rate limit aplicado.
/// Contrato de la implementación: asignar la <see cref="CommandSequence"/> y encolar como **una sola operación atómica**,
/// de modo que el orden de las Sequence coincida con el orden real de la cola. Capacidad finita.
/// No detecta duplicados: eso lo hace Simulation (D-043).
/// </summary>
public interface ICommandSink
{
  CommandAdmission Admit(PlayerId player, ClientCommandId clientCommandId, IGameCommand command);
}

/// <summary>
/// Lado consumidor de la cola global. Su único consumidor es el hilo de Simulation (06 §4).
/// Devuelve hasta <paramref name="maxCount"/> comandos en orden creciente de Sequence; los demás quedan para ticks siguientes.
/// </summary>
public interface ICommandSource
{
  IReadOnlyList<CommandEnvelope> DequeueBatch(int maxCount);
}

/// <summary>Resultado de admitir un comando en la cola global.</summary>
public sealed record CommandAdmission
{
  private CommandAdmission(CommandSequence? sequence, ErrorCode? error)
  {
    Sequence = sequence;
    Error = error;
  }

  public bool IsAdmitted => Error is null;

  public CommandSequence? Sequence { get; }

  public ErrorCode? Error { get; }

  public static CommandAdmission Admitted(CommandSequence sequence) => new(sequence, null);

  public static CommandAdmission Rejected(ErrorCode error)
  {
    if (error.IsDefault)
    {
      throw new ArgumentException("El rechazo requiere código de error.", nameof(error));
    }

    return new CommandAdmission(null, error);
  }
}
