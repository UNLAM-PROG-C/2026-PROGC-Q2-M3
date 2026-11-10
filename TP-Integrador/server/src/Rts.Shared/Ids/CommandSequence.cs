namespace Rts.Shared.Ids;

/// <summary>
/// Secuencia global de admisión de comandos. DECIDIDO (06 §4, 08 §4): la asigna Command Admission
/// en el mismo orden real de encolado; Simulation procesa en orden creciente de Sequence.
/// También define la prioridad entre productores que compiten por comida (05 §3).
/// </summary>
public readonly record struct CommandSequence(long Value) : IComparable<CommandSequence>
{
  public static readonly CommandSequence First = new(1);

  public CommandSequence Next() => new(Value + 1);

  public int CompareTo(CommandSequence other) => Value.CompareTo(other.Value);

  public static bool operator <(CommandSequence left, CommandSequence right) => left.Value < right.Value;
  public static bool operator >(CommandSequence left, CommandSequence right) => left.Value > right.Value;
  public static bool operator <=(CommandSequence left, CommandSequence right) => left.Value <= right.Value;
  public static bool operator >=(CommandSequence left, CommandSequence right) => left.Value >= right.Value;

  public override string ToString() => $"S{Value}";
}
