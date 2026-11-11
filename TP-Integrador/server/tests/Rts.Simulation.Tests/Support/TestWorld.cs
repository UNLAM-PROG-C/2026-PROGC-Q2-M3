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

  /// <summary>Árbol disponible del mundo de prueba (para comprobar que el mapa no cambia).</summary>
  public static TreeEconomyState TreeState(WorldState world) => world.Trees[Tree].Economy;
}
