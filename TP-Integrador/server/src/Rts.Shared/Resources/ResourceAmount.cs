namespace Rts.Shared.Resources;

/// <summary>
/// Cantidad de recursos (madera y comida) en enteros. DECIDIDO (01 §2): enteros, sin capacidad máxima.
/// Sirve tanto para saldos como para costos y deltas; los deltas pueden ser negativos.
/// Las reglas de asequibilidad y gasto (p. ej. <c>CanAfford</c>) pertenecen a Economy, no a este tipo.
/// </summary>
public readonly record struct ResourceAmount(int Wood, int Food)
{
  public static readonly ResourceAmount Zero = default;

  public static ResourceAmount OfWood(int wood) => new(wood, 0);

  public static ResourceAmount OfFood(int food) => new(0, food);

  public bool IsZero => Wood == 0 && Food == 0;

  /// <summary>Verdadero si ninguna componente es negativa (invariante de los saldos de jugador).</summary>
  public bool IsNonNegative => Wood >= 0 && Food >= 0;

  public static ResourceAmount operator +(ResourceAmount left, ResourceAmount right) =>
      new(checked(left.Wood + right.Wood), checked(left.Food + right.Food));

  public static ResourceAmount operator -(ResourceAmount left, ResourceAmount right) =>
      new(checked(left.Wood - right.Wood), checked(left.Food - right.Food));

  public static ResourceAmount operator -(ResourceAmount value) =>
      new(checked(-value.Wood), checked(-value.Food));

  public override string ToString() => $"Wood={Wood}, Food={Food}";
}
