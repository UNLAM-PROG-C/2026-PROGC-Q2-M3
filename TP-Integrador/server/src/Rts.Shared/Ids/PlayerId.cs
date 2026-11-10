namespace Rts.Shared.Ids;

/// <summary>
/// Identidad oficial de un jugador. DECIDIDO (07 §2): el primer cliente registrado es 1 y el segundo es 2.
/// Siempre se deriva de la conexión registrada, nunca de un campo enviado por el cliente.
/// </summary>
public readonly record struct PlayerId(int Value) : IComparable<PlayerId>
{
    public static readonly PlayerId One = new(1);
    public static readonly PlayerId Two = new(2);

    /// <summary>Solo 1 y 2 son jugadores válidos en una partida 1v1.</summary>
    public bool IsValid => Value is 1 or 2;

    /// <summary>Devuelve el rival en la partida 1v1.</summary>
    /// <exception cref="InvalidOperationException">Si el ID no es 1 ni 2.</exception>
    public PlayerId Opponent() => Value switch
    {
        1 => Two,
        2 => One,
        _ => throw new InvalidOperationException($"PlayerId inválido: {Value}."),
    };

    public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"P{Value}";
}
