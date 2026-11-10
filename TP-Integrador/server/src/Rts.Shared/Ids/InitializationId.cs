namespace Rts.Shared.Ids;

/// <summary>
/// Identifica un intento de inicialización de partida durante <c>Starting</c>. DECIDIDO (07 §3-4):
/// <c>ClientReady</c> debe referirlo; al abandonar <c>Starting</c> se invalida y la próxima preparación usa uno nuevo.
/// RECOMENDADO (contrato 00 §3): contador creciente dentro de la sesión.
/// </summary>
public readonly record struct InitializationId(long Value) : IComparable<InitializationId>
{
  public static readonly InitializationId First = new(1);

  public InitializationId Next() => new(Value + 1);

  public int CompareTo(InitializationId other) => Value.CompareTo(other.Value);

  public override string ToString() => $"Init{Value}";
}
