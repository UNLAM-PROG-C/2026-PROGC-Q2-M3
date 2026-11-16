using System.Collections.Immutable;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Match;

namespace Rts.Simulation.State;

/// <summary>
/// Único estado autoritativo de la partida (06 §1). DECIDIDO (D-044): es **inmutable**. Cada fase produce un
/// <see cref="WorldState"/> nuevo y el commit reemplaza la referencia vigente.
/// <list type="bullet">
/// <item>Si algo falla a mitad de un tick, se descarta el estado en construcción y el último confirmado queda intacto.</item>
/// <item>El snapshot de la fase 4 es la misma referencia: Economy y Combat leen un objeto que nadie puede modificar.</item>
/// <item>Las colecciones están ordenadas por ID, así que su recorrido es determinista.</item>
/// </list>
/// El constructor verifica la integridad: ambos jugadores presentes, claves coherentes, IDs únicos entre unidades,
/// edificios y árboles, y ningún ID mayor que <see cref="LastEntityId"/>.
/// </summary>
public sealed record WorldState
{
  public WorldState(
      TickId tick,
      GridSpec grid,
      MapRevision mapRevision,
      ImmutableSortedDictionary<PlayerId, PlayerState> players,
      ImmutableSortedDictionary<EntityId, UnitEntity> units,
      ImmutableSortedDictionary<EntityId, BuildingEntity> buildings,
      ImmutableSortedDictionary<EntityId, TreeEntity> trees,
      EntityId lastEntityId,
      OrderId lastOrderId,
      IdempotencyLedger idempotency,
      MatchResult? gameplayResult)
  {
    ArgumentNullException.ThrowIfNull(grid);
    ArgumentNullException.ThrowIfNull(players);
    ArgumentNullException.ThrowIfNull(units);
    ArgumentNullException.ThrowIfNull(buildings);
    ArgumentNullException.ThrowIfNull(trees);
    ArgumentNullException.ThrowIfNull(idempotency);

    if (players.Count != 2 || !players.ContainsKey(PlayerId.One) || !players.ContainsKey(PlayerId.Two) ||
        players.Any(kv => kv.Value.Player != kv.Key))
    {
      throw new ArgumentException("El mundo tiene exactamente a P1 y P2.", nameof(players));
    }

    var seen = new HashSet<EntityId>();
    void Check(EntityId key, EntityId id, string name)
    {
      if (key != id || !seen.Add(id) || id > lastEntityId)
      {
        throw new ArgumentException($"ID inconsistente o duplicado en {name}: {id}.");
      }
    }

    foreach ((EntityId key, UnitEntity unit) in units)
    {
      Check(key, unit.Id, nameof(units));
    }

    foreach ((EntityId key, BuildingEntity building) in buildings)
    {
      Check(key, building.Id, nameof(buildings));
    }

    foreach ((EntityId key, TreeEntity tree) in trees)
    {
      Check(key, tree.Id, nameof(trees));
    }

    Tick = tick;
    Grid = grid;
    MapRevision = mapRevision;
    Players = players;
    Units = units;
    Buildings = buildings;
    Trees = trees;
    LastEntityId = lastEntityId;
    LastOrderId = lastOrderId;
    Idempotency = idempotency;
    GameplayResult = gameplayResult;
  }

  /// <summary>Último tick confirmado (<see cref="TickId.Zero"/> = estado inicial).</summary>
  public TickId Tick { get; }

  public GridSpec Grid { get; }

  public MapRevision MapRevision { get; }

  public ImmutableSortedDictionary<PlayerId, PlayerState> Players { get; }

  public ImmutableSortedDictionary<EntityId, UnitEntity> Units { get; }

  public ImmutableSortedDictionary<EntityId, BuildingEntity> Buildings { get; }

  public ImmutableSortedDictionary<EntityId, TreeEntity> Trees { get; }

  /// <summary>Último EntityId asignado; el próximo es <c>LastEntityId.Value + 1</c>.</summary>
  public EntityId LastEntityId { get; }

  /// <summary>Último OrderId asignado.</summary>
  public OrderId LastOrderId { get; }

  public IdempotencyLedger Idempotency { get; }

  /// <summary>Resultado de gameplay fijado por Victory Rules; con valor, no se ejecutan más fases de gameplay.</summary>
  public MatchResult? GameplayResult { get; }

  public bool IsGameplayOver => GameplayResult is not null;

  /// <summary>
  /// Copia con las partes indicadas reemplazadas; las omitidas se conservan. Pasa por el constructor, así que la copia
  /// vuelve a verificar todas las invariantes. Es la única forma de "modificar" el mundo: el original nunca cambia (D-044).
  /// </summary>
  internal WorldState With(
      TickId? tick = null,
      MapRevision? mapRevision = null,
      ImmutableSortedDictionary<PlayerId, PlayerState>? players = null,
      ImmutableSortedDictionary<EntityId, UnitEntity>? units = null,
      ImmutableSortedDictionary<EntityId, BuildingEntity>? buildings = null,
      ImmutableSortedDictionary<EntityId, TreeEntity>? trees = null,
      EntityId? lastEntityId = null,
      OrderId? lastOrderId = null,
      IdempotencyLedger? idempotency = null,
      MatchResult? gameplayResult = null) => new(
          tick ?? Tick,
          Grid,
          mapRevision ?? MapRevision,
          players ?? Players,
          units ?? Units,
          buildings ?? Buildings,
          trees ?? Trees,
          lastEntityId ?? LastEntityId,
          lastOrderId ?? LastOrderId,
          idempotency ?? Idempotency,
          gameplayResult ?? GameplayResult);
}
