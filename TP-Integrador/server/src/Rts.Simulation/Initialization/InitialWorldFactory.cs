using System.Collections.Immutable;
using Rts.Combat;
using Rts.Construction;
using Rts.Economy;
using Rts.Economy.Model;
using Rts.Movement.Model;
using Rts.Production.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Map;
using Rts.Simulation.State;

namespace Rts.Simulation.Initialization;

/// <summary>
/// Crea el <see cref="WorldState"/> del tick 0 a partir del mapa y de los valores de cada módulo dueño (contrato 06 §8):
/// <list type="bullet">
/// <item>un árbol disponible por celda del mapa, con <c>MaxWoodPerTree</c> de madera y la posición en el centro de la celda (D-050);</item>
/// <item>por jugador, su Centro Principal completado y operativo desde el tick 0, con HP máximo de Construction y productor vacío;</item>
/// <item>por jugador, sus unidades iniciales en reposo, con HP máximo de Combat y cooldown listo (D-022);</item>
/// <item>el mismo saldo inicial de Economy para ambos (06 §2).</item>
/// </list>
/// Los IDs se asignan en orden determinista: árboles en el orden del archivo, después P1 (Centro Principal y unidades) y
/// después P2. Crear dos veces con las mismas entradas da el mismo mundo, así que puede repetirse en cada preparación de Starting.
/// </summary>
public sealed class InitialWorldFactory
{
  private readonly MapDefinition _map;
  private readonly int _maxWoodPerTree;
  private readonly IEconomyRules _economy;
  private readonly IConstructionRules _construction;
  private readonly ICombatRules _combat;

  public InitialWorldFactory(
      MapDefinition map,
      int maxWoodPerTree,
      IEconomyRules economy,
      IConstructionRules construction,
      ICombatRules combat)
  {
    ArgumentNullException.ThrowIfNull(map);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWoodPerTree);
    _map = map;
    _maxWoodPerTree = maxWoodPerTree;
    _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    _construction = construction ?? throw new ArgumentNullException(nameof(construction));
    _combat = combat ?? throw new ArgumentNullException(nameof(combat));
  }

  public WorldState Create()
  {
    EntityId lastId = EntityId.None;
    var trees = ImmutableSortedDictionary.CreateBuilder<EntityId, TreeEntity>();
    var buildings = ImmutableSortedDictionary.CreateBuilder<EntityId, BuildingEntity>();
    var units = ImmutableSortedDictionary.CreateBuilder<EntityId, UnitEntity>();

    foreach (GridCell cell in _map.TreeCells)
    {
      lastId = lastId.Next();
      trees.Add(lastId, CreateTree(lastId, cell));
    }

    foreach (PlayerStartDefinition start in _map.Starts.OrderBy(start => start.Player))
    {
      lastId = lastId.Next();
      buildings.Add(lastId, CreateMainCenter(lastId, start));
      foreach (InitialUnitDefinition unit in start.InitialUnits)
      {
        lastId = lastId.Next();
        units.Add(lastId, CreateUnit(lastId, start.Player, unit));
      }
    }

    return new WorldState(
        TickId.Zero, _map.Grid, MapRevision.Initial, CreatePlayers(), units.ToImmutable(), buildings.ToImmutable(),
        trees.ToImmutable(), lastId, OrderId.None, IdempotencyLedger.Empty, gameplayResult: null);
  }

  private TreeEntity CreateTree(EntityId id, GridCell cell) =>
      new(id, cell, _map.Grid.CellCenter(cell), TreeEconomyState.Available(id, _maxWoodPerTree));

  private BuildingEntity CreateMainCenter(EntityId id, PlayerStartDefinition start)
  {
    const BuildingType Type = BuildingType.MainCenter;
    var footprint = new Footprint(start.MainCenterAnchor, _construction.GetFootprintSize(Type));
    var producer = new ProducerState(id, start.Player, Type, footprint, TickId.Zero, activeJob: null, pending: EquatableArray<ProductionRequest>.Empty);
    return new BuildingEntity(
        id, start.Player, Type, footprint, _construction.GetMaxHp(Type), operationalFromTick: TickId.Zero,
        farmEconomy: null, repair: null, production: producer, towerReadyAtTick: null);
  }

  private UnitEntity CreateUnit(EntityId id, PlayerId owner, InitialUnitDefinition unit) => new(
      id, owner, unit.UnitType, unit.Position, _combat.GetUnitMaxHp(unit.UnitType), MovementOrder.Idle,
      route: EquatableArray<PositionXZ>.Empty, routeMapRevision: MapRevision.Initial, repathPendingSinceTick: null,
      lastPursuitRepathTick: null, gatherTicksAccumulated: 0, readyAtTick: TickId.Zero);

  private ImmutableSortedDictionary<PlayerId, PlayerState> CreatePlayers() =>
      ImmutableSortedDictionary.CreateRange(
      [
        KeyValuePair.Create(PlayerId.One, new PlayerState(PlayerId.One, _economy.InitialBalance)),
        KeyValuePair.Create(PlayerId.Two, new PlayerState(PlayerId.Two, _economy.InitialBalance)),
      ]);
}
