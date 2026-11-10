namespace Rts.Shared.Ids;

/// <summary>
/// Identificador de comando generado por el cliente (el <c>MessageId</c> de los mensajes de gameplay).
/// DECIDIDO (D-005): entero creciente por cliente, en el rango [1, 2^53] para que el JSON de Godot
/// (que parsea números como float64) no pierda precisión. Networking valida el rango.
/// </summary>
public readonly record struct ClientCommandId(long Value)
{
  /// <summary>Máximo entero representable exactamente como double (2^53).</summary>
  public const long MaxValue = 1L << 53;

  public bool IsInValidRange => Value is >= 1 and <= MaxValue;

  public override string ToString() => $"C{Value}";
}
