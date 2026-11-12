using Rts.Economy.Model;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Initialization;
using Rts.Simulation.Map;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;

namespace Rts.Simulation.Tests.Support;

/// <summary>
/// Mundo chico y conocido para probar Simulation. IDs fijos, porque la fábrica los asigna en orden:
/// árbol 1; P1: Centro Principal 2 y trabajadores 3 y 4; P2: Centro Principal 5 y trabajador 6.
/// </summary>
internal static class TestWorld
{
  public const int MaxWoodPerTree = 100;

  public static readonly EntityId Tree = new(1);
  public static readonly EntityId P1Center = new(2);
  public static readonly EntityId P1WorkerA = new(3);
  public static readonly EntityId P1WorkerB = new(4);
  public static readonly EntityId P2Center = new(5);
  public static readonly EntityId P2Worker = new(6);
  public static readonly EntityId Missing = new(99);

  public static readonly PositionXZ SomeDestination = new(20.5, 20.5);

  public static readonly MapDefinition Map = new(
      new GridSpec(1.0, 40, 40),
      [new GridCell(10, 20)],
      [
        new PlayerStartDefinition(PlayerId.One, new GridCell(2, 2),
        [
          new InitialUnitDefinition(UnitType.Worker, new PositionXZ(7.5, 3.5)),
          new InitialUnitDefinition(UnitType.Worker, new PositionXZ(7.5, 4.5)),
        ]),
        new PlayerStartDefinition(PlayerId.Two, new GridCell(34, 34),
          [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(32.5, 36.5))]),
      ]);

  public static InitialWorldFactory Factory() =>
      new(Map, MaxWoodPerTree, new FakeEconomyRules(), new FakeConstructionRules(), new FakeCombatRules());

  public static WorldState Create() => Factory().Create();

  /// <summary>El mundo con la unidad caminando hacia <see cref="SomeDestination"/> (orden Move con ruta).</summary>
  public static WorldState WithMovingUnit(WorldState world, EntityId unitId)
  {
    OrderId orderId = world.LastOrderId.Next();
    UnitEntity unit = world.Units[unitId];
    var moving = new UnitEntity(
        unit.Id, unit.Owner, unit.Type, unit.Position, unit.Hp, MovementOrder.Move(orderId, SomeDestination),
        [SomeDestination], world.MapRevision, repathPendingSinceTick: null, lastPursuitRepathTick: null,
        gatherTicksAccumulated: 0, unit.ReadyAtTick);
    return world.With(units: world.Units.SetItem(unitId, moving), lastOrderId: orderId);
  }

  /// <summary>El mundo con la unidad recolectando el árbol, con el contador de recolección avanzado.</summary>
  public static WorldState WithGatheringUnit(WorldState world, EntityId unitId, int gatherTicks)
  {
    OrderId orderId = world.LastOrderId.Next();
    UnitEntity unit = world.Units[unitId];
    var gathering = new UnitEntity(
        unit.Id, unit.Owner, unit.Type, unit.Position, unit.Hp, MovementOrder.Task(orderId, UnitOrderKind.GatherWood, Tree),
        EquatableArray<PositionXZ>.Empty, world.MapRevision, repathPendingSinceTick: null, lastPursuitRepathTick: null,
        gatherTicks, unit.ReadyAtTick);
    return world.With(units: world.Units.SetItem(unitId, gathering), lastOrderId: orderId);
  }

  /// <summary>El mundo con la unidad con una orden de tarea sobre <paramref name="targetId"/>, sin ruta.</summary>
  public static WorldState WithTaskOrder(WorldState world, EntityId unitId, UnitOrderKind kind, EntityId targetId)
  {
    OrderId orderId = world.LastOrderId.Next();
    UnitEntity unit = world.Units[unitId];
    var task = new UnitEntity(
        unit.Id, unit.Owner, unit.Type, unit.Position, unit.Hp, MovementOrder.Task(orderId, kind, targetId),
        EquatableArray<PositionXZ>.Empty, world.MapRevision, repathPendingSinceTick: null, lastPursuitRepathTick: null,
        gatherTicksAccumulated: 0, unit.ReadyAtTick);
    return world.With(units: world.Units.SetItem(unitId, task), lastOrderId: orderId);
  }

  /// <summary>Un punto a 1 unidad del centro del árbol: dentro del rango de prueba de <c>FakeMovementRules</c>.</summary>
  public static readonly PositionXZ NextToTree = new(11.5, 20.5);

  public const int FarmBatches = 5;
  public const int FarmHp = 200;
  private static readonly FootprintSize FarmSize = new(2, 2);

  /// <summary>El mundo con la unidad parada en <paramref name="position"/> (todo lo demás igual).</summary>
  public static WorldState WithUnitAt(WorldState world, EntityId unitId, PositionXZ position)
  {
    UnitEntity unit = world.Units[unitId];
    var moved = new UnitEntity(
        unit.Id, unit.Owner, unit.Type, position, unit.Hp, unit.Order, unit.Route, unit.RouteMapRevision,
        unit.RepathPendingSinceTick, unit.LastPursuitRepathTick, unit.GatherTicksAccumulated, unit.ReadyAtTick);
    return world.With(units: world.Units.SetItem(unitId, moved));
  }

  /// <summary>
  /// El mundo con una granja nueva de <paramref name="owner"/>: completada y produciendo (con su estado de Economy), o en obra
  /// (sin él). Devuelve también su ID.
  /// </summary>
  public static (WorldState World, EntityId FarmId) WithFarm(WorldState world, PlayerId owner, GridCell anchor, bool completed = true)
  {
    EntityId farmId = world.LastEntityId.Next();
    FarmEconomyState? economy = completed
        ? new FarmEconomyState(farmId, owner, FarmProductionStatus.Producing, FarmBatches, ticksAccumulated: 0, TickId.Zero)
        : null;
    var farm = new BuildingEntity(
        farmId, owner, BuildingType.Farm, new Footprint(anchor, FarmSize), FarmHp, completed ? TickId.Zero : null,
        economy, repair: null, production: null, towerReadyAtTick: null);
    return (world.With(buildings: world.Buildings.SetItem(farmId, farm), lastEntityId: farmId), farmId);
  }

  public const int TowerHp = 300;
  private static readonly FootprintSize TowerSize = new(1, 1);

  /// <summary>El mundo con un soldado nuevo, quieto o con orden Attack sobre <paramref name="attackTarget"/>. Devuelve también su ID.</summary>
  public static (WorldState World, EntityId SoldierId) WithSoldier(
      WorldState world, PlayerId owner, PositionXZ position, EntityId? attackTarget = null)
  {
    EntityId soldierId = world.LastEntityId.Next();
    OrderId lastOrderId = attackTarget is null ? world.LastOrderId : world.LastOrderId.Next();
    MovementOrder order = attackTarget is { } target ? MovementOrder.Task(lastOrderId, UnitOrderKind.Attack, target) : MovementOrder.Idle;
    var soldier = new UnitEntity(
        soldierId, owner, UnitType.Soldier, position, FakeCombatRules.SoldierMaxHp, order, EquatableArray<PositionXZ>.Empty,
        world.MapRevision, repathPendingSinceTick: null, lastPursuitRepathTick: null, gatherTicksAccumulated: 0, TickId.Zero);
    return (world.With(units: world.Units.SetItem(soldierId, soldier), lastEntityId: soldierId, lastOrderId: lastOrderId), soldierId);
  }

  /// <summary>El mundo con una torre nueva de <paramref name="owner"/>: completada (lista para disparar) o en obra. Devuelve su ID.</summary>
  public static (WorldState World, EntityId TowerId) WithTower(WorldState world, PlayerId owner, GridCell anchor, bool completed = true)
  {
    EntityId towerId = world.LastEntityId.Next();
    TickId? completedAt = completed ? TickId.Zero : null;
    var tower = new BuildingEntity(
        towerId, owner, BuildingType.Tower, new Footprint(anchor, TowerSize), TowerHp, completedAt,
        farmEconomy: null, repair: null, production: null, towerReadyAtTick: completedAt);
    return (world.With(buildings: world.Buildings.SetItem(towerId, tower), lastEntityId: towerId), towerId);
  }

  /// <summary>Árbol disponible del mundo de prueba (para comprobar que el mapa no cambia).</summary>
  public static TreeEconomyState TreeState(WorldState world) => world.Trees[Tree].Economy;
}
