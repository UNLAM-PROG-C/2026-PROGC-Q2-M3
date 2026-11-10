namespace Rts.Shared.Ids;

/// <summary>
/// Número de tick de Simulation. Los intervalos de gameplay se expresan en ticks, nunca en tiempo de reloj.
/// <see cref="Zero"/> es el tick del estado inicial (antes de Running).
/// </summary>
public readonly record struct TickId(long Value) : IComparable<TickId>
{
    public static readonly TickId Zero = default;

    public TickId Next() => new(Value + 1);

    public TickId Plus(long ticks) => new(Value + ticks);

    public int CompareTo(TickId other) => Value.CompareTo(other.Value);

    /// <summary>Cantidad de ticks transcurridos entre <paramref name="earlier"/> y <paramref name="later"/>.</summary>
    public static long operator -(TickId later, TickId earlier) => later.Value - earlier.Value;

    public static bool operator <(TickId left, TickId right) => left.Value < right.Value;
    public static bool operator >(TickId left, TickId right) => left.Value > right.Value;
    public static bool operator <=(TickId left, TickId right) => left.Value <= right.Value;
    public static bool operator >=(TickId left, TickId right) => left.Value >= right.Value;

    public override string ToString() => $"T{Value}";
}
