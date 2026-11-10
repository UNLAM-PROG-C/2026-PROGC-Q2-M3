using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Combat.Model;

/// <summary>
/// Vista de combate inmutable de una unidad viva en el snapshot: posible objetivo de soldados y torres.
/// <see cref="Position"/> es la posición oficial ya actualizada por Movement en este tick (02 §5).
/// </summary>
public sealed record CombatUnit
{
  public CombatUnit(EntityId id, PlayerId owner, UnitType type, PositionXZ position, int hp)
  {
    if (id.IsNone)
    {
      throw new ArgumentException("La unidad debe tener un EntityId válido.", nameof(id));
    }

    if (!owner.IsValid)
    {
      throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
    }

    if (!Enum.IsDefined(type))
    {
      throw new ArgumentException($"Tipo de unidad inválido: {type}.", nameof(type));
    }

    if (!position.IsFinite)
    {
      throw new ArgumentException("La posición debe ser finita.", nameof(position));
    }

    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hp);

    Id = id;
    Owner = owner;
    Type = type;
    Position = position;
    Hp = hp;
  }

  public EntityId Id { get; }

  public PlayerId Owner { get; }

  public UnitType Type { get; }

  public PositionXZ Position { get; }

  /// <summary>HP actual (&gt; 0: las unidades destruidas no están en el snapshot).</summary>
  public int Hp { get; }
}
