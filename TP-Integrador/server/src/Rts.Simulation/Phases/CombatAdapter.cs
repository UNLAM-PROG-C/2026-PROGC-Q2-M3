using System.Collections.Immutable;
using Rts.Combat;
using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>
/// Adapter entre Simulation y el módulo Combat: ocupa la rama Combat de la fase paralela (06 §4, contrato 02 §3 y §5).
/// Combat (el módulo) solo calcula ataques y daño; este adapter arma su entrada desde el mundo y aplica su propuesta.
/// <list type="bullet">
/// <item><b>Calcular</b> (otra tarea): todas las unidades y edificios, los soldados con orden Attack y las torres completadas.</item>
/// <item><b>Confirmar</b> (hilo de Simulation, <b>después</b> de Economy): cooldowns, soldados sin objetivo, HP y destrucciones, con
/// su cascada: se quitan las entidades, se cancelan las órdenes que las apuntaban y, si cayó un edificio, avanza la
/// <c>MapRevision</c>. Los eventos de esa cascada los emite la barrera al final.</item>
/// </list>
/// </summary>
internal sealed class CombatAdapter : IDomainBranch<CombatTickResult>
{
  private readonly ICombatCalculator _calculator;

  public CombatAdapter(ICombatCalculator calculator)
  {
    _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
  }

  public CombatTickResult Calculate(WorldState snapshot, TickId tick)
  {
    var input = new CombatTickInput(
        tick,
        snapshot.Grid,
        [.. snapshot.Units.Values.Select(unit => new CombatUnit(unit.Id, unit.Owner, unit.Type, unit.Position, unit.Hp))],
        [.. snapshot.Buildings.Values.Select(building => new CombatBuilding(
            building.Id, building.Owner, building.Type, building.Footprint, building.Hp, building.OperationalFromTick))],
        [.. snapshot.Units.Values
            .Where(unit => unit.Type == UnitType.Soldier && unit.Order.Kind == UnitOrderKind.Attack && unit.Order.TargetId is not null)
            .Select(unit => new SoldierAttacker(unit.Id, unit.Order.TargetId!.Value, unit.ReadyAtTick))],
        [.. snapshot.Buildings.Values
            .Where(building => building.TowerReadyAtTick is not null)
            .Select(tower => new TowerAttacker(tower.Id, tower.TowerReadyAtTick!.Value))]);
    return _calculator.CalculateTick(input);
  }

  public void Commit(TickContext context, CombatTickResult result)
  {
    WorldState world = context.World;
    EnsureCoherent(world, context.Tick, result);
    var units = world.Units;
    var buildings = world.Buildings;

    foreach (CooldownUpdate cooldown in result.CooldownUpdates)
    {
      if (units.TryGetValue(cooldown.AttackerId, out UnitEntity? soldier))
      {
        units = units.SetItem(soldier.Id, soldier.WithReadyAt(cooldown.ReadyAtTick));
      }
      else
      {
        buildings = buildings.SetItem(cooldown.AttackerId, buildings[cooldown.AttackerId].WithTowerReadyAt(cooldown.ReadyAtTick));
      }
    }

    foreach (EntityId soldierId in result.SoldiersToIdle)
    {
      units = Cancel(units, soldierId, context);
    }

    var destroyed = new HashSet<EntityId>();
    foreach (TargetDamage damage in result.Damage)
    {
      if (damage.IsDestroyed)
      {
        destroyed.Add(damage.TargetId);
        units = units.Remove(damage.TargetId);
        buildings = buildings.Remove(damage.TargetId);
      }
      else if (units.TryGetValue(damage.TargetId, out UnitEntity? unit))
      {
        units = units.SetItem(unit.Id, unit.WithHp(damage.HpAfter));
      }
      else
      {
        buildings = buildings.SetItem(damage.TargetId, buildings[damage.TargetId].WithHp(damage.HpAfter));
      }
    }

    units = CancelOrdersOnDestroyed(units, destroyed, context);
    bool buildingFell = destroyed.Any(world.Buildings.ContainsKey);
    context.World = world.With(
        units: units,
        buildings: buildings,
        mapRevision: buildingFell ? world.MapRevision.Next() : world.MapRevision);

    foreach (IDomainEventPayload proposed in result.ProposedEvents)
    {
      context.Emit(proposed);
    }
  }

  /// <summary>
  /// D-025: toda unidad viva cuya orden apuntaba a algo destruido pasa a Idle: soldados que lo atacaban, constructores y
  /// reparadores. La cola de un productor destruido desaparece con el edificio, sin reembolso.
  /// </summary>
  private static ImmutableSortedDictionary<EntityId, UnitEntity> CancelOrdersOnDestroyed(
      ImmutableSortedDictionary<EntityId, UnitEntity> units,
      HashSet<EntityId> destroyed,
      TickContext context)
  {
    if (destroyed.Count == 0)
    {
      return units;
    }

    EntityId[] orphaned = [.. units.Values
        .Where(unit => unit.Order.TargetId is { } target && destroyed.Contains(target))
        .Select(unit => unit.Id)];
    foreach (EntityId unitId in orphaned)
    {
      units = Cancel(units, unitId, context);
    }

    return units;
  }

  private static ImmutableSortedDictionary<EntityId, UnitEntity> Cancel(
      ImmutableSortedDictionary<EntityId, UnitEntity> units,
      EntityId unitId,
      TickContext context)
  {
    UnitEntity unit = units[unitId];
    if (unit.Order.IsIdle)
    {
      return units;
    }

    context.CanceledOrders.Add(unitId);
    return units.SetItem(unitId, unit.AsIdle());
  }

  /// <summary>
  /// Coherencia (06 §4, fase 6): mismo tick, objetivos y atacantes existentes y HP de partida igual al del mundo. Como Economy no
  /// toca HP, el mundo al confirmar tiene el mismo HP que el snapshot que vio Combat.
  /// </summary>
  private static void EnsureCoherent(WorldState world, TickId tick, CombatTickResult result)
  {
    Require(result.Tick == tick, $"Combat respondió el tick {result.Tick} en lugar de {tick}.");
    foreach (TargetDamage damage in result.Damage)
    {
      int? hp = world.Units.TryGetValue(damage.TargetId, out UnitEntity? unit) ? unit.Hp
          : world.Buildings.TryGetValue(damage.TargetId, out BuildingEntity? building) ? building.Hp
          : null;
      Require(hp == damage.HpBefore, $"Combat dañó a {damage.TargetId} partiendo de {damage.HpBefore} HP, pero el mundo tiene {hp?.ToString() ?? "nada"}.");
    }

    foreach (CooldownUpdate cooldown in result.CooldownUpdates)
    {
      bool isSoldier = world.Units.TryGetValue(cooldown.AttackerId, out UnitEntity? soldier) && soldier.Type == UnitType.Soldier;
      bool isTower = world.Buildings.TryGetValue(cooldown.AttackerId, out BuildingEntity? tower) && tower.TowerReadyAtTick is not null;
      Require(isSoldier || isTower, $"Combat actualizó el cooldown de {cooldown.AttackerId}, que no es un soldado ni una torre completada.");
    }

    foreach (EntityId soldierId in result.SoldiersToIdle)
    {
      Require(world.Units.ContainsKey(soldierId), $"Combat pidió Idle para la unidad inexistente {soldierId}.");
    }
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
