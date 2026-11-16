using Rts.Protocol.Outbound;
using Rts.Protocol.State;
using Rts.Shared.Collections;
using Rts.Shared.Entities;

namespace Rts.StateSync.Tests.Integration;

/// <summary>
/// Cliente simulado SOLO para pruebas: arranca con su InitialState y aplica los <c>StateEvents</c> y <c>PositionUpdate</c> como lo
/// haría Godot. Falla con <see cref="InvalidOperationException"/> si recibe algo que un cliente real no podría aplicar: un tick que
/// retrocede, eventos desordenados, una entidad desconocida o un dato privado de otro jugador.
/// </summary>
internal sealed class FakeClient
{
  private readonly SortedDictionary<int, UnitDto> _units = [];
  private readonly SortedDictionary<int, BuildingDto> _buildings = [];
  private readonly SortedDictionary<int, TreeDto> _trees = [];
  private readonly SortedDictionary<int, ProducerState> _production = [];
  private long _mapRevision;
  private ResourcesDto _resources;
  private long _lastTick;
  private long _lastPositionTick;

  public FakeClient(InitialStateDto initial)
  {
    Player = initial.PlayerId;
    _lastTick = initial.Tick;
    _lastPositionTick = initial.Tick;
    _mapRevision = initial.Map.Revision;
    _resources = initial.OwnResources;

    foreach (UnitDto unit in initial.Units)
    {
      _units.Add(unit.UnitId, unit);
    }
    foreach (BuildingDto building in initial.Buildings)
    {
      _buildings.Add(building.BuildingId, building);
    }
    foreach (TreeDto tree in initial.Trees)
    {
      _trees.Add(tree.TreeId, tree);
    }
    foreach (ProducerDto producer in initial.OwnProduction)
    {
      _production.Add(producer.ProducerId, new ProducerState(producer.ProducerId, producer.Status, producer.ActiveUnitType,
          producer.RemainingTicks, [.. producer.Pending]));
    }
  }

  public int Player { get; }

  /// <summary><c>clientCommandId</c> de cada <c>CommandResult</c> recibido, en orden.</summary>
  public List<long> CommandResultIds { get; } = [];

  /// <summary>Cantidad de <c>RepathRequested</c> recibidos (todos de unidades propias: si no, el cliente falla).</summary>
  public int RepathCount { get; private set; }

  public void Apply(OutboundMessage message)
  {
    switch (message.Payload)
    {
      case StateEventsDto batch:
        ApplyBatch(batch);
        break;
      case PositionUpdateDto positions:
        ApplyPositions(positions);
        break;
      default:
        throw new InvalidOperationException($"Mensaje inesperado durante la partida: {message.Type}.");
    }
  }

  /// <summary>Lo que ambos jugadores deberían ver igual: mapa, unidades, edificios y árboles.</summary>
  public PublicWorld Public() => new(_mapRevision, [.. _units.Values], [.. _buildings.Values], [.. _trees.Values]);

  /// <summary>Lo que solo ve este jugador: sus recursos y su producción.</summary>
  public PrivateWorld Private() => new(_resources, [.. _production.Values]);

  private void ApplyBatch(StateEventsDto batch)
  {
    Require(batch.Tick > _lastTick, $"lote del tick {batch.Tick} después del tick {_lastTick}");
    _lastTick = batch.Tick;

    int previousIndex = -1;
    foreach (StateEventDto e in batch.Events)
    {
      Require(e.Index > previousIndex, $"evento con index {e.Index} después de {previousIndex}");
      previousIndex = e.Index;
      ApplyEvent(e);
    }

    Require(_mapRevision == batch.MapRevision, $"revisión {_mapRevision} distinta de la del lote ({batch.MapRevision})");
    CommandResultIds.AddRange(batch.CommandResults.Select(r => r.ClientCommandId));
  }

  private void ApplyEvent(StateEventDto e)
  {
    switch (e)
    {
      case BuildingPlacedDto p:
        Require(_buildings.TryAdd(p.BuildingId, new BuildingDto(p.BuildingId, p.Owner, p.BuildingType, p.Footprint, p.Hp, false,
            p.BuildingType == BuildingType.Farm ? new FarmDto(false, false) : null)), $"edificio {p.BuildingId} repetido");
        break;
      case BuildingProgressChangedDto p:
        _buildings[p.BuildingId] = Building(p.BuildingId) with { Hp = p.Hp };
        break;
      case BuildingCompletedDto p:
        _buildings[p.BuildingId] = Building(p.BuildingId) with { IsCompleted = true };
        break;
      case FarmDepletedDto p:
        _buildings[p.FarmId] = Building(p.FarmId) with { Farm = Farm(p.FarmId) with { IsDepleted = true } };
        break;
      case FarmRepairStartedDto p:
        _buildings[p.FarmId] = Building(p.FarmId) with { Farm = Farm(p.FarmId) with { IsRepairing = true } };
        break;
      case FarmRepairProgressChangedDto p:
        Farm(p.FarmId);
        break;
      case FarmRepairCompletedDto p:
        _buildings[p.FarmId] = Building(p.FarmId) with { Farm = new FarmDto(false, false) };
        break;
      case MapRevisionChangedDto p:
        Require(p.Revision > _mapRevision, $"revisión {p.Revision} después de {_mapRevision}");
        _mapRevision = p.Revision;
        break;
      case EntityDamagedDto p:
        ApplyDamage(p);
        break;
      case EntityDestroyedDto p:
        Require(_units.Remove(p.EntityId) || _buildings.Remove(p.EntityId), $"se destruyó la entidad desconocida {p.EntityId}");
        break;
      case TreeDepletedDto p:
        _trees[p.TreeId] = Tree(p.TreeId) with { IsAvailable = false };
        break;
      case TreeRespawnedDto p:
        _trees[p.TreeId] = Tree(p.TreeId) with { IsAvailable = true };
        break;
      case UnitOrderChangedDto p:
        _units[p.UnitId] = Unit(p.UnitId) with { Order = p.Kind };
        break;
      case UnitSpawnedDto p:
        Require(_units.TryAdd(p.UnitId, new UnitDto(p.UnitId, p.Owner, p.UnitType, p.X, p.Z, p.Hp, UnitOrderKind.Idle)),
            $"unidad {p.UnitId} repetida");
        break;
      case RepathRequestedDto p:
        Require(Unit(p.UnitId).Owner == Player, $"repath de la unidad {p.UnitId}, que no es del jugador {Player}");
        RepathCount++;
        break;
      case ProductionChangedDto p:
        Require(_production.ContainsKey(p.ProducerId), $"producción del productor {p.ProducerId}, que no es del jugador {Player}");
        _production[p.ProducerId] = new ProducerState(p.ProducerId, p.Status, p.ActiveUnitType, p.RemainingTicks, [.. p.Pending]);
        break;
      case ResourcesChangedDto p:
        _resources = new ResourcesDto(p.Wood, p.Food);
        break;
      default:
        throw new InvalidOperationException($"Evento sin aplicar en el cliente: {e.GetType().Name}.");
    }
  }

  private void ApplyDamage(EntityDamagedDto damage)
  {
    if (_units.TryGetValue(damage.EntityId, out UnitDto? unit))
    {
      _units[damage.EntityId] = unit with { Hp = damage.Hp };
      return;
    }
    _buildings[damage.EntityId] = Building(damage.EntityId) with { Hp = damage.Hp };
  }

  private void ApplyPositions(PositionUpdateDto update)
  {
    Require(update.Tick >= _lastTick && update.Tick > _lastPositionTick, $"posiciones del tick {update.Tick} fuera de orden");
    _lastTick = update.Tick;
    _lastPositionTick = update.Tick;

    foreach (UnitPositionDto position in update.Units)
    {
      _units[position.Id] = Unit(position.Id) with { X = position.X, Z = position.Z };
    }
  }

  private UnitDto Unit(int id) => Known(_units, id, "unidad");

  private BuildingDto Building(int id) => Known(_buildings, id, "edificio");

  private TreeDto Tree(int id) => Known(_trees, id, "árbol");

  private FarmDto Farm(int id) => Building(id).Farm ?? throw new InvalidOperationException($"El edificio {id} no es una granja.");

  private static T Known<T>(SortedDictionary<int, T> entities, int id, string kind) =>
      entities.TryGetValue(id, out T? entity) ? entity : throw new InvalidOperationException($"El cliente no conoce la {kind} {id}.");

  private static void Require(bool condition, string problem)
  {
    if (!condition)
    {
      throw new InvalidOperationException($"El cliente no puede aplicar el mensaje: {problem}.");
    }
  }
}

/// <summary>Estado público de un cliente, ordenado por id para poder compararlo.</summary>
internal sealed record PublicWorld(long MapRevision, EquatableArray<UnitDto> Units, EquatableArray<BuildingDto> Buildings,
    EquatableArray<TreeDto> Trees);

/// <summary>Estado privado de un cliente: sus recursos y su producción.</summary>
internal sealed record PrivateWorld(ResourcesDto Resources, EquatableArray<ProducerState> Production);

/// <summary>Como <see cref="ProducerDto"/>, pero con la cola comparable por valor.</summary>
internal sealed record ProducerState(int ProducerId, ProducerStatus Status, UnitType? ActiveUnitType, int? RemainingTicks,
    EquatableArray<UnitType> Pending);
