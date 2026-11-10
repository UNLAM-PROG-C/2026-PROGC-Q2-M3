namespace Rts.Shared.Ids;

/// <summary>
/// Revisión del mapa de obstáculos. DECIDIDO (03 §2, 04 §4-5): cambia al colocar o destruir edificios/vallas;
/// las rutas candidatas de Godot indican la revisión que usaron. <see cref="Initial"/> corresponde al mapa inicial.
/// </summary>
public readonly record struct MapRevision(long Value) : IComparable<MapRevision>
{
  public static readonly MapRevision Initial = default;

  public MapRevision Next() => new(Value + 1);

  public int CompareTo(MapRevision other) => Value.CompareTo(other.Value);

  public static bool operator <(MapRevision left, MapRevision right) => left.Value < right.Value;
  public static bool operator >(MapRevision left, MapRevision right) => left.Value > right.Value;
  public static bool operator <=(MapRevision left, MapRevision right) => left.Value <= right.Value;
  public static bool operator >=(MapRevision left, MapRevision right) => left.Value >= right.Value;

  public override string ToString() => $"R{Value}";
}
