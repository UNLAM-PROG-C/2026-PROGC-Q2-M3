namespace Rts.Shared.Ids;

/// <summary>
/// Clave de idempotencia de comandos. DECIDIDO (AGENTS §3, 06 §4): <c>PlayerId + ClientCommandId</c>.
/// Un duplicado no produce una segunda acción; se responde con el resultado original.
/// </summary>
public readonly record struct IdempotencyKey(PlayerId Player, ClientCommandId ClientCommandId)
{
    public override string ToString() => $"{Player}/{ClientCommandId}";
}
