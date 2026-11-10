using Rts.Shared.Ids;

namespace Rts.Shared.Events;

/// <summary>
/// Identidad y orden estable de un evento confirmado (06 §8): tick de commit e índice dentro del tick.
/// El par (Tick, IndexInTick) es único y define el orden oficial de publicación.
/// </summary>
public readonly record struct EventStamp(TickId Tick, int IndexInTick) : IComparable<EventStamp>
{
    public int CompareTo(EventStamp other)
    {
        int byTick = Tick.CompareTo(other.Tick);
        return byTick != 0 ? byTick : IndexInTick.CompareTo(other.IndexInTick);
    }

    public override string ToString() => $"{Tick}#{IndexInTick}";
}
