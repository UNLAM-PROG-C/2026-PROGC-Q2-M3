using Rts.Economy.Model;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;

namespace Rts.Simulation.State;

/// <summary>
/// Árbol en el <see cref="WorldState"/> oficial. Conserva ID, celda y posición aunque esté agotado (01 §3).
/// RECOMENDADO (D-050): un árbol reserva exactamente una celda y su posición es el centro de esa celda.
/// </summary>
public sealed record TreeEntity
{
  public TreeEntity(EntityId id, GridCell cell, PositionXZ position, TreeEconomyState economy)
  {
    ArgumentNullException.ThrowIfNull(economy);
    if (id.IsNone || economy.TreeId != id)
    {
      throw new ArgumentException("El árbol y su estado económico deben compartir un EntityId válido.", nameof(economy));
    }

    if (!position.IsFinite)
    {
      throw new ArgumentException("La posición debe ser finita.", nameof(position));
    }

    Id = id;
    Cell = cell;
    Position = position;
    Economy = economy;
  }

  public EntityId Id { get; }

  public GridCell Cell { get; }

  public PositionXZ Position { get; }

  public TreeEconomyState Economy { get; }

  /// <summary>Copia del árbol con el estado económico que confirmó Economy; celda y posición no cambian.</summary>
  internal TreeEntity WithEconomy(TreeEconomyState economy) => new(Id, Cell, Position, economy);
}

/// <summary>Saldo oficial de un jugador. Invariante: nunca negativo (01 §8).</summary>
public sealed record PlayerState
{
  public PlayerState(PlayerId player, ResourceAmount resources)
  {
    if (!player.IsValid)
    {
      throw new ArgumentException($"Jugador inválido: {player}.", nameof(player));
    }

    if (!resources.IsNonNegative)
    {
      throw new ArgumentException("El saldo de un jugador no puede ser negativo.", nameof(resources));
    }

    Player = player;
    Resources = resources;
  }

  public PlayerId Player { get; }

  public ResourceAmount Resources { get; }

  /// <summary>Saldo después de sumar <paramref name="delta"/>. Si quedara negativo, el constructor lo rechaza.</summary>
  internal PlayerState Plus(ResourceAmount delta) => new(Player, Resources + delta);
}
