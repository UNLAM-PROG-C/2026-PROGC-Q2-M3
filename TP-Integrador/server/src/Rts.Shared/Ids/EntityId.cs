namespace Rts.Shared.Ids;

/// <summary>
/// Identificador estable de una entidad del mundo (unidad, edificio o árbol).
/// Un único espacio de IDs global para todos los tipos de entidad (D-003): permite desempates
/// deterministas por "menor ID" y eventos genéricos como <c>EntityDestroyed</c>.
/// Lo asigna exclusivamente el servidor (Simulation). <c>0</c> significa "sin entidad".
/// </summary>
public readonly record struct EntityId(int Value) : IComparable<EntityId>
{
  public static readonly EntityId None = default;

  public bool IsNone => Value == 0;

  public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

  public static bool operator <(EntityId left, EntityId right) => left.Value < right.Value;
  public static bool operator >(EntityId left, EntityId right) => left.Value > right.Value;
  public static bool operator <=(EntityId left, EntityId right) => left.Value <= right.Value;
  public static bool operator >=(EntityId left, EntityId right) => left.Value >= right.Value;

  public override string ToString() => $"E{Value}";
}
