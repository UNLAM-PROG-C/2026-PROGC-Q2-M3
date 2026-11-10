namespace Rts.Shared.Ids;

/// <summary>
/// Identificador de una conexión TCP aceptada por Networking. GameSession lo asocia a un <see cref="PlayerId"/>.
/// No contiene ni referencia objetos de socket (07 §8).
/// </summary>
public readonly record struct ConnectionId(long Value)
{
    public override string ToString() => $"Conn{Value}";
}
