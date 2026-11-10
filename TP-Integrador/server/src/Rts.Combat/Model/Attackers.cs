using Rts.Shared.Ids;

namespace Rts.Combat.Model;

/// <summary>
/// Soldado con una orden <c>Attack</c> activa sobre <see cref="TargetId"/> (02 §2: sin orden explícita no ataca).
/// Su posición y su dueño se toman de su <see cref="CombatUnit"/> en la misma entrada.
/// <para>
/// <see cref="ReadyAtTick"/>: primer tick en que puede volver a atacar (RECOMENDADO D-022). Pertenece al soldado, no a la
/// orden, por lo que cambiar de objetivo no lo reinicia (02 §3).
/// </para>
/// </summary>
public sealed record SoldierAttacker
{
  public SoldierAttacker(EntityId soldierId, EntityId targetId, TickId readyAtTick)
  {
    if (soldierId.IsNone)
    {
      throw new ArgumentException("El soldado debe tener un EntityId válido.", nameof(soldierId));
    }

    if (targetId.IsNone)
    {
      throw new ArgumentException("La orden Attack debe tener un objetivo.", nameof(targetId));
    }

    if (soldierId == targetId)
    {
      throw new ArgumentException("Un soldado no puede atacarse a sí mismo.", nameof(targetId));
    }

    SoldierId = soldierId;
    TargetId = targetId;
    ReadyAtTick = readyAtTick;
  }

  public EntityId SoldierId { get; }

  public EntityId TargetId { get; }

  public TickId ReadyAtTick { get; }
}

/// <summary>
/// Estado de ataque de una torre completada. Su huella, dueño y operatividad se toman de su <see cref="CombatBuilding"/>.
/// <see cref="ReadyAtTick"/> tiene la misma semántica que en <see cref="SoldierAttacker"/>.
/// </summary>
public sealed record TowerAttacker
{
  public TowerAttacker(EntityId towerId, TickId readyAtTick)
  {
    if (towerId.IsNone)
    {
      throw new ArgumentException("La torre debe tener un EntityId válido.", nameof(towerId));
    }

    TowerId = towerId;
    ReadyAtTick = readyAtTick;
  }

  public EntityId TowerId { get; }

  public TickId ReadyAtTick { get; }
}
