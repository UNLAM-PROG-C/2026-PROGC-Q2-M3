using Rts.Production.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production.Contracts;

/// <summary>Comida disponible de un jugador al entrar a la fase Production (ya incluye lo producido por Economy en este tick, 05 §3).</summary>
public sealed record PlayerFood
{
  public PlayerFood(PlayerId player, int food)
  {
    if (!player.IsValid)
    {
      throw new ArgumentException($"Jugador inválido: {player}.", nameof(player));
    }

    ArgumentOutOfRangeException.ThrowIfNegative(food);
    Player = player;
    Food = food;
  }

  public PlayerId Player { get; }

  public int Food { get; }
}

/// <summary>
/// Entrada inmutable de la fase 8 (Production). Solo se construye si la partida sigue tras Victory Rules (05 §6).
/// <list type="bullet">
/// <item><see cref="Producers"/>: productores completados que siguen existiendo tras Combat; un productor destruido
/// en este tick no aparece (05 §3).</item>
/// <item><see cref="Food"/>: comida de cada jugador dueño de algún productor.</item>
/// <item><see cref="Spawn"/>: ocupación vigente para buscar celdas de aparición.</item>
/// </list>
/// Los costos (Economy, D-015) y las duraciones (<c>ProductionConfig</c>) se inyectan en la implementación.
/// </summary>
public sealed record ProductionTickInput
{
  public ProductionTickInput(
      TickId tick,
      EquatableArray<ProducerState> producers,
      EquatableArray<PlayerFood> food,
      SpawnView spawn)
  {
    ArgumentNullException.ThrowIfNull(spawn);

    var producerIds = new HashSet<EntityId>();
    foreach (ProducerState producer in producers)
    {
      ArgumentNullException.ThrowIfNull(producer, nameof(producers));
      if (!producerIds.Add(producer.ProducerId))
      {
        throw new ArgumentException($"Productor duplicado: {producer.ProducerId}.", nameof(producers));
      }
    }

    var players = new HashSet<PlayerId>();
    foreach (PlayerFood entry in food)
    {
      ArgumentNullException.ThrowIfNull(entry, nameof(food));
      if (!players.Add(entry.Player))
      {
        throw new ArgumentException($"Comida duplicada para {entry.Player}.", nameof(food));
      }
    }

    foreach (ProducerState producer in producers.Where(p => !players.Contains(p.Owner)))
    {
      throw new ArgumentException($"Falta la comida de {producer.Owner}, dueño de {producer.ProducerId}.", nameof(food));
    }

    Tick = tick;
    Producers = producers;
    Food = food;
    Spawn = spawn;
  }

  public TickId Tick { get; }

  public EquatableArray<ProducerState> Producers { get; }

  public EquatableArray<PlayerFood> Food { get; }

  public SpawnView Spawn { get; }
}

/// <summary>
/// Propuesta inmutable de la fase 8 (05 §7). Simulation confirma cada inicio en un commit atómico con su gasto,
/// y cada aparición en el orden dado (las celdas ya vienen reservadas sin repetirse).
/// </summary>
/// <param name="Tick">Tick de la entrada.</param>
/// <param name="Starts">Entrenamientos iniciados, con su comida a cobrar, por Sequence ascendente.</param>
/// <param name="Spawns">Unidades a crear, por Sequence de su solicitud ascendente.</param>
/// <param name="ProducerUpdates">Nuevo estado de cada productor que cambió (por ProducerId ascendente).</param>
/// <param name="ProposedEvents"><c>ProductionChanged</c> por ProducerId ascendente (solo dueño).</param>
public sealed record ProductionTickResult(
    TickId Tick,
    EquatableArray<ProductionStart> Starts,
    EquatableArray<SpawnProposal> Spawns,
    EquatableArray<ProducerState> ProducerUpdates,
    EquatableArray<IDomainEventPayload> ProposedEvents)
{
  public static ProductionTickResult Empty(TickId tick) => new(tick, default, default, default, default);
}

/// <summary>Inicio de entrenamiento: Simulation cobra <see cref="PaidFood"/> y activa la tarea en un único commit (05 §3).</summary>
public sealed record ProductionStart(EntityId ProducerId, PlayerId Owner, CommandSequence RequestSequence, UnitType UnitType, int PaidFood);

/// <summary>Aparición propuesta: Simulation crea la unidad (ID nuevo, HP máximo de Combat) en <see cref="Position"/>, centro de <see cref="Cell"/>.</summary>
public sealed record SpawnProposal(
    EntityId ProducerId,
    PlayerId Owner,
    CommandSequence RequestSequence,
    UnitType UnitType,
    GridCell Cell,
    PositionXZ Position);
