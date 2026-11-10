using Rts.Shared.Ids;

namespace Rts.Shared.Commands;

/// <summary>
/// Comando admitido y listo para Simulation (06 §8). Lo crea Command Admission tras validar la estructura:
/// <list type="bullet">
/// <item><see cref="Sequence"/>: secuencia global asignada en el mismo orden de encolado.</item>
/// <item><see cref="Player"/>: identidad oficial derivada de la conexión, nunca del cliente.</item>
/// <item><see cref="ClientCommandId"/>: ID del cliente para idempotencia y correlación de respuestas.</item>
/// </list>
/// </summary>
public sealed record CommandEnvelope(
    CommandSequence Sequence,
    PlayerId Player,
    ClientCommandId ClientCommandId,
    IGameCommand Command)
{
  public IdempotencyKey Key => new(Player, ClientCommandId);

  public CommandType Type => Command.Type;
}
