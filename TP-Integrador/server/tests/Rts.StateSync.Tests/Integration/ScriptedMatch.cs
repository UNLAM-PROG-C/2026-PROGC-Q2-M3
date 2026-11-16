using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.World;

namespace Rts.StateSync.Tests.Integration;

/// <summary>
/// Partida guionada SOLO para pruebas: hace de Simulation. Con una semilla fija genera, tick a tick, eventos coherentes con su
/// propio mundo (la verdad) y los entrega como <see cref="TickPublication"/>. Al final devuelve ese mundo como <see cref="WorldView"/>.
/// No aplica reglas de juego (colisiones, costos exactos, alcances): solo produce secuencias que un cliente debe poder aplicar.
/// </summary>
internal sealed class ScriptedMatch
{
  private const double CellSize = 1.0;
  private const int Columns = 40;
  private const int Rows = 30;
  private const int MainCenterSize = 4;

  /// <summary>Celdas entre cada Centro Principal y su esquina del mapa.</summary>
  private const int MainCenterMargin = 2;

  /// <summary>Saldo inicial alto para que los cobros nunca dejen recursos negativos.</summary>
  private const int InitialStock = 100_000;
  private const int TreeCount = 6;
  private const int FirstEntityId = 100;
  private const int MaxActionsPerTick = 3;
  private const int MaxDamage = 15;

  /// <summary>Los Centros Principales nunca se destruyen: así la partida no termina y ambos jugadores siguen produciendo.</summary>
  private const int MainCenterMinHp = 1;
  private const int ConstructionStep = 50;
  private const int TrainingTicks = 40;
  private const int WoodPerTree = 10;

  /// <summary>Paso de movimiento por tick. Es exacto en binario, así que el redondeo a decimales no lo altera.</summary>
  private const double MoveStep = 0.25;
  private const double CellCenter = 0.5;

  private static readonly UnitType[] UnitTypes = [UnitType.Worker, UnitType.Soldier];
  private static readonly BuildingType[] BuildableTypes = [BuildingType.Farm, BuildingType.Barracks, BuildingType.Tower, BuildingType.Fence];
  private static readonly UnitOrderKind[] OrderKinds = Enum.GetValues<UnitOrderKind>();
  private static readonly PlayerId[] Players = [PlayerId.One, PlayerId.Two];

  private readonly Random _random;
  private readonly GameCatalog _catalog;
  private readonly GridSpec _grid = new(CellSize, Columns, Rows);
  private readonly SortedDictionary<EntityId, UnitView> _units = [];
  private readonly SortedDictionary<EntityId, BuildingView> _buildings = [];
  private readonly SortedDictionary<EntityId, TreeView> _trees = [];
  private readonly SortedDictionary<EntityId, ProducerView> _producers = [];
  private readonly Dictionary<PlayerId, ResourceAmount> _resources = [];
  private readonly Dictionary<PlayerId, List<long>> _issuedCommands = [];
  private readonly List<DomainEvent> _events = [];
  private readonly List<CommandResult> _results = [];
  private readonly Action[] _actions;
  private TickId _tick = TickId.Zero;
  private MapRevision _revision = MapRevision.Initial;
  private int _nextEntityId = FirstEntityId;
  private long _nextOrderId;
  private long _nextSequence;

  public ScriptedMatch(int seed, GameCatalog catalog)
  {
    _random = new Random(seed);
    _catalog = catalog;
    _actions = [Produce, Damage, ChangeOrder, RequestRepath, PlaceBuilding, Construct, CycleFarm, CycleTree, ResolveCommand];

    AddMainCenter(PlayerId.One, new GridCell(MainCenterMargin, MainCenterMargin));
    AddMainCenter(PlayerId.Two, new GridCell(Columns - MainCenterSize - MainCenterMargin, Rows - MainCenterSize - MainCenterMargin));
    for (int i = 0; i < TreeCount; i++)
    {
      var cell = new GridCell(Columns / 2, i * Rows / TreeCount);
      EntityId id = NewId();
      _trees.Add(id, new TreeView(id, cell, CenterOf(cell), true));
    }
    foreach (PlayerId player in Players)
    {
      _resources[player] = new ResourceAmount(InitialStock, InitialStock);
      _issuedCommands[player] = [];
      Spawn(player, UnitType.Worker, emit: false);
    }
  }

  /// <summary>Tipos de payload emitidos al menos una vez, para verificar que la partida ejercitó todos los eventos.</summary>
  public HashSet<Type> EmittedPayloadTypes { get; } = [];

  /// <summary><c>clientCommandId</c> resueltos para cada jugador, en orden.</summary>
  public IReadOnlyList<long> IssuedCommands(PlayerId player) => _issuedCommands[player];

  /// <summary>El mundo actual, como lo publicaría Simulation para un InitialState.</summary>
  public WorldView World() => new(
      _tick,
      new MapView(_grid, _revision),
      [.. Players.Select(p => new PlayerView(p, _resources[p]))],
      [.. _units.Values],
      [.. _buildings.Values],
      [.. _trees.Values],
      [.. _producers.Values]);

  /// <summary>Avanza un tick: hace hasta <see cref="MaxActionsPerTick"/> acciones al azar, mueve todas las unidades y publica.</summary>
  public TickPublication NextTick()
  {
    _tick = _tick.Next();
    _events.Clear();
    _results.Clear();

    int actions = _random.Next(MaxActionsPerTick + 1);
    for (int i = 0; i < actions; i++)
    {
      _actions[_random.Next(_actions.Length)]();
    }

    foreach ((EntityId id, UnitView unit) in _units.ToList())
    {
      _units[id] = unit with { Position = Step(unit.Position) };
    }

    return new TickPublication(_tick, _revision, [.. _events], [.. _results],
        [.. _units.Values.Select(u => new UnitPosition(u.UnitId, u.Position))], null);
  }

  /// <summary>Un productor libre empieza a entrenar (cobra comida); uno que entrena termina y aparece la unidad.</summary>
  private void Produce()
  {
    ProducerView producer = Pick(_producers.Values);
    if (producer.Status == ProducerStatus.Idle)
    {
      UnitType type = UnitTypes[_random.Next(UnitTypes.Length)];
      Charge(producer.Owner, UnitEntry(type).Cost);
      SetProduction(producer with { Status = ProducerStatus.Training, ActiveUnitType = type, RemainingTicks = TrainingTicks });
      return;
    }

    Spawn(producer.Owner, producer.ActiveUnitType!.Value, emit: true);
    SetProduction(producer with { Status = ProducerStatus.Idle, ActiveUnitType = null, RemainingTicks = null });
  }

  /// <summary>Daña una unidad o un edificio; si llega a 0 lo destruye. Los Centros Principales nunca bajan de 1.</summary>
  private void Damage()
  {
    EntityId[] targets = [.. _units.Keys, .. _buildings.Keys];
    EntityId target = targets[_random.Next(targets.Length)];
    int damage = _random.Next(1, MaxDamage + 1);

    if (_units.TryGetValue(target, out UnitView? unit))
    {
      int hp = unit.Hp - damage;
      Emit(new EntityDamaged(target, Math.Max(hp, 0)));
      if (hp > 0)
      {
        _units[target] = unit with { Hp = hp };
        return;
      }
      _units.Remove(target);
      Emit(new EntityDestroyed(target));
      return;
    }

    BuildingView building = _buildings[target];
    int buildingHp = building.BuildingType == BuildingType.MainCenter ? Math.Max(building.Hp - damage, MainCenterMinHp) : building.Hp - damage;
    Emit(new EntityDamaged(target, Math.Max(buildingHp, 0)));
    if (buildingHp > 0)
    {
      _buildings[target] = building with { Hp = buildingHp };
      return;
    }
    _buildings.Remove(target);
    Emit(new EntityDestroyed(target));
    ChangeMap();
  }

  private void ChangeOrder()
  {
    if (_units.Count == 0)
    {
      return;
    }
    UnitView unit = Pick(_units.Values);
    UnitOrderKind kind = OrderKinds[_random.Next(OrderKinds.Length)];
    _units[unit.UnitId] = unit with { Order = kind };
    Emit(new UnitOrderChanged(unit.UnitId, NewOrderId(), kind));
  }

  private void RequestRepath()
  {
    if (_units.Count == 0)
    {
      return;
    }
    UnitView unit = Pick(_units.Values);
    Emit(new RepathRequested(unit.UnitId, unit.Owner, NewOrderId(), _revision, CenterOf(RandomCell()), null, false));
  }

  /// <summary>Coloca un edificio en obra (cobra madera); ocupa espacio desde ya, así que cambia el mapa.</summary>
  private void PlaceBuilding()
  {
    PlayerId owner = Players[_random.Next(Players.Length)];
    BuildingCatalogEntry entry = BuildingEntry(BuildableTypes[_random.Next(BuildableTypes.Length)]);
    var anchor = new GridCell(_random.Next(Columns - entry.Footprint.Width), _random.Next(Rows - entry.Footprint.Height));
    var footprint = new Footprint(anchor, entry.Footprint);
    EntityId id = NewId();
    int hp = ConstructionStep;

    _buildings.Add(id, new BuildingView(id, owner, entry.BuildingType, footprint, hp, false,
        entry.BuildingType == BuildingType.Farm ? new FarmPublicState(false, false) : null));
    Emit(new BuildingPlaced(id, owner, entry.BuildingType, footprint, hp));
    ChangeMap();
    Charge(owner, entry.Cost!.Value);
  }

  /// <summary>Avanza una obra; al llegar al HP máximo la completa.</summary>
  private void Construct()
  {
    BuildingView[] sites = [.. _buildings.Values.Where(b => !b.IsCompleted)];
    if (sites.Length == 0)
    {
      return;
    }
    BuildingView site = sites[_random.Next(sites.Length)];
    int maxHp = BuildingEntry(site.BuildingType).MaxHp;
    int hp = Math.Min(site.Hp + ConstructionStep, maxHp);

    _buildings[site.BuildingId] = site with { Hp = hp, IsCompleted = hp == maxHp };
    Emit(new BuildingProgressChanged(site.BuildingId, hp));
    if (hp == maxHp)
    {
      Emit(new BuildingCompleted(site.BuildingId));
    }
  }

  /// <summary>Ciclo de una granja completa: se agota, empieza la reparación (cobra) y la termina.</summary>
  private void CycleFarm()
  {
    BuildingView[] farms = [.. _buildings.Values.Where(b => b.Farm is not null && b.IsCompleted)];
    if (farms.Length == 0)
    {
      return;
    }
    BuildingView farm = farms[_random.Next(farms.Length)];
    FarmPublicState state = farm.Farm!;

    if (!state.IsDepleted)
    {
      _buildings[farm.BuildingId] = farm with { Farm = state with { IsDepleted = true } };
      Emit(new FarmDepleted(farm.BuildingId));
      return;
    }
    if (!state.IsRepairing)
    {
      _buildings[farm.BuildingId] = farm with { Farm = state with { IsRepairing = true } };
      Emit(new FarmRepairStarted(farm.BuildingId));
      Charge(farm.Owner, _catalog.FarmRepairCost);
      return;
    }
    _buildings[farm.BuildingId] = farm with { Farm = new FarmPublicState(false, false) };
    Emit(new FarmRepairProgressChanged(farm.BuildingId, _catalog.FarmRepairWorkRequired, _catalog.FarmRepairWorkRequired));
    Emit(new FarmRepairCompleted(farm.BuildingId));
  }

  /// <summary>Un árbol disponible se agota (acredita madera a un jugador); uno agotado reaparece.</summary>
  private void CycleTree()
  {
    TreeView tree = Pick(_trees.Values);
    _trees[tree.TreeId] = tree with { IsAvailable = !tree.IsAvailable };
    if (!tree.IsAvailable)
    {
      Emit(new TreeRespawned(tree.TreeId));
      return;
    }
    Emit(new TreeDepleted(tree.TreeId));
    Charge(Players[_random.Next(Players.Length)], -ResourceAmount.OfWood(WoodPerTree));
  }

  /// <summary>Resuelve un comando de un jugador al azar: su resultado debe llegarle solo a él.</summary>
  private void ResolveCommand()
  {
    PlayerId player = Players[_random.Next(Players.Length)];
    List<long> issued = _issuedCommands[player];
    var clientCommandId = new ClientCommandId(issued.Count + 1);
    issued.Add(clientCommandId.Value);
    _results.Add(new CommandResult(new IdempotencyKey(player, clientCommandId), new CommandSequence(++_nextSequence), _tick,
        CommandOutcome.Accepted, null, default));
  }

  private void AddMainCenter(PlayerId owner, GridCell anchor)
  {
    BuildingCatalogEntry entry = BuildingEntry(BuildingType.MainCenter);
    EntityId id = NewId();
    _buildings.Add(id, new BuildingView(id, owner, BuildingType.MainCenter, new Footprint(anchor, entry.Footprint), entry.MaxHp, true, null));
    _producers.Add(id, new ProducerView(id, owner, ProducerStatus.Idle, null, null, default));
  }

  private void Spawn(PlayerId owner, UnitType type, bool emit)
  {
    BuildingView mainCenter = _buildings.Values.Single(b => b.Owner == owner && b.BuildingType == BuildingType.MainCenter);
    PositionXZ position = CenterOf(mainCenter.Footprint.Anchor);
    EntityId id = NewId();
    int hp = UnitEntry(type).MaxHp;

    _units.Add(id, new UnitView(id, owner, type, position, hp, UnitOrderKind.Idle));
    if (emit)
    {
      Emit(new UnitSpawned(id, owner, type, position, hp));
    }
  }

  private void SetProduction(ProducerView producer)
  {
    _producers[producer.ProducerId] = producer;
    Emit(new ProductionChanged(producer.ProducerId, producer.Owner, producer.Status, producer.ActiveUnitType, producer.RemainingTicks,
        producer.PendingUnitTypes));
  }

  private void Charge(PlayerId player, ResourceAmount cost)
  {
    _resources[player] -= cost;
    Emit(new ResourcesChanged(player, _resources[player]));
  }

  private void ChangeMap()
  {
    _revision = _revision.Next();
    Emit(new MapRevisionChanged(_revision));
  }

  private void Emit(IDomainEventPayload payload)
  {
    _events.Add(new DomainEvent(new EventStamp(_tick, _events.Count), payload));
    EmittedPayloadTypes.Add(payload.GetType());
  }

  /// <summary>Mueve un paso en una dirección al azar, sin salir del mapa.</summary>
  private PositionXZ Step(PositionXZ position)
  {
    double dx = RandomDirection() * MoveStep;
    double dz = RandomDirection() * MoveStep;
    return new PositionXZ(Math.Clamp(position.X + dx, 0, Columns * CellSize), Math.Clamp(position.Z + dz, 0, Rows * CellSize));
  }

  /// <summary>-1, 0 o 1 con igual probabilidad.</summary>
  private int RandomDirection() => _random.Next(-1, 2);

  private T Pick<T>(IEnumerable<T> values)
  {
    T[] all = [.. values];
    return all[_random.Next(all.Length)];
  }

  private GridCell RandomCell() => new(_random.Next(Columns), _random.Next(Rows));

  private static PositionXZ CenterOf(GridCell cell) => new((cell.Column + CellCenter) * CellSize, (cell.Row + CellCenter) * CellSize);

  private UnitCatalogEntry UnitEntry(UnitType type) => _catalog.Units.Single(u => u.UnitType == type);

  private BuildingCatalogEntry BuildingEntry(BuildingType type) => _catalog.Buildings.Single(b => b.BuildingType == type);

  private EntityId NewId() => new(_nextEntityId++);

  private OrderId NewOrderId() => new(++_nextOrderId);
}
