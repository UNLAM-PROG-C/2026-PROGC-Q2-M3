namespace Rts.Shared.Ids;

/// <summary>
/// Identificador de una orden activa de unidad. DECIDIDO (04 §4): lo asigna el servidor;
/// una respuesta de repath con un OrderId viejo o equivocado se rechaza. <c>0</c> significa "sin orden".
/// </summary>
public readonly record struct OrderId(long Value) : IComparable<OrderId>
{
  public static readonly OrderId None = default;

  public bool IsNone => Value == 0;

  public OrderId Next() => new(Value + 1);

  public int CompareTo(OrderId other) => Value.CompareTo(other.Value);

  public override string ToString() => $"O{Value}";
}
