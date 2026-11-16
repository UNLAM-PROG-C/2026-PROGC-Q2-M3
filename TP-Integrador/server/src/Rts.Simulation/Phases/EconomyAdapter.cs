using System.Collections.Immutable;
using Rts.Economy;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Movement;
using Rts.Movement.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>
/// Adapter entre Simulation y el módulo Economy: ocupa la rama Economy de la fase paralela (06 §4, contrato 01 §3 y §5).
/// Economy (el módulo) solo calcula; este adapter arma su entrada desde el mundo y aplica su propuesta.
/// <list type="bullet">
/// <item><b>Calcular</b> (otra tarea): arma <see cref="EconomyTickInput"/> desde el snapshot (solo recolectores <b>en rango</b>,
/// todos los árboles y las granjas completadas) y llama al calculador de Economy.</item>
/// <item><b>Confirmar</b> (hilo de Simulation): verifica la coherencia y aplica saldos, árboles, granjas y contadores. Los
/// recolectores que se quedan sin árbol pasan a Idle; su evento lo emite la cascada de la barrera.</item>
/// </list>
/// </summary>
internal sealed class EconomyAdapter : IDomainBranch<EconomyTickResult>
{
  private readonly IEconomyCalculator _calculator;
  private readonly IMovementRules _movement;

  public EconomyAdapter(IEconomyCalculator calculator, IMovementRules movement)
  {
    _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
  }

  public EconomyTickResult Calculate(WorldState snapshot, TickId tick)
  {
    var input = new EconomyTickInput(
        tick,
        [.. GatherersInRange(snapshot)],
        [.. snapshot.Trees.Values.Select(tree => tree.Economy)],
        [.. snapshot.Buildings.Values.Select(building => building.FarmEconomy).OfType<FarmEconomyState>()]);
    return _calculator.CalculateTick(input);
  }

  public void Commit(TickContext context, EconomyTickResult result)
  {
    WorldState world = context.World;
    EnsureCoherent(world, context.Tick, result);

    var units = ApplyGatherCounters(world.Units, result);
    var trees = world.Trees;
    foreach (TreeEconomyState update in result.TreeUpdates)
    {
      trees = trees.SetItem(update.TreeId, trees[update.TreeId].WithEconomy(update));
    }

    var buildings = world.Buildings;
    foreach (FarmEconomyState update in result.FarmUpdates)
    {
      buildings = buildings.SetItem(update.FarmId, buildings[update.FarmId].WithFarmEconomy(update));
    }

    var players = world.Players;
    foreach (PlayerResourceDelta credit in result.PlayerDeltas)
    {
      players = players.SetItem(credit.Player, players[credit.Player].Plus(credit.Delta));
    }

    units = IdleWorkersWithoutTree(units, result, context);
    context.World = world.With(units: units, trees: trees, buildings: buildings, players: players);
    foreach (IDomainEventPayload proposed in result.ProposedEvents)
    {
      context.Emit(proposed);
    }
  }

  /// <summary>
  /// Recolectores habilitados (D-016): orden GatherWood sobre un árbol que existe y posición en rango de recolección según
  /// Movement. Los que todavía caminan hacia el árbol no entran. Movement es seguro para usar desde esta tarea.
  /// </summary>
  private IEnumerable<GathererState> GatherersInRange(WorldState snapshot)
  {
    double gatherRange = _movement.GetInteractionRange(UnitOrderKind.GatherWood);
    foreach (UnitEntity unit in snapshot.Units.Values)
    {
      if (unit.Order.Kind != UnitOrderKind.GatherWood
          || unit.Order.TargetId is not { } treeId
          || !snapshot.Trees.TryGetValue(treeId, out TreeEntity? tree))
      {
        continue;
      }

      MovementTarget target = MovementTarget.AtPoint(tree.Id, tree.Position);
      if (_movement.IsWithinInteractionRange(snapshot.Grid, unit.Position, target, gatherRange))
      {
        yield return new GathererState(unit.Id, unit.Owner, treeId, unit.GatherTicksAccumulated);
      }
    }
  }

  private static ImmutableSortedDictionary<EntityId, UnitEntity> ApplyGatherCounters(
      ImmutableSortedDictionary<EntityId, UnitEntity> units,
      EconomyTickResult result)
  {
    foreach (GathererState update in result.GathererUpdates)
    {
      units = units.SetItem(update.WorkerId, units[update.WorkerId].WithGatherTicks(update.TicksAccumulated));
    }

    return units;
  }

  /// <summary>
  /// Pasa a Idle a los <c>WorkersToIdle</c> y, además, a <b>todos</b> los que tienen GatherWood sobre un árbol que se agotó en este
  /// tick, también a los que todavía caminan hacia él: Economy solo ve a los que están en rango (01 §5).
  /// </summary>
  private static ImmutableSortedDictionary<EntityId, UnitEntity> IdleWorkersWithoutTree(
      ImmutableSortedDictionary<EntityId, UnitEntity> units,
      EconomyTickResult result,
      TickContext context)
  {
    var depletedNow = result.TreeUpdates
        .Where(tree => tree.Status == TreeStatus.Depleted && tree.DepletedAtTick == context.Tick)
        .Select(tree => tree.TreeId)
        .ToHashSet();

    IEnumerable<EntityId> toIdle = result.WorkersToIdle.Concat(units.Values
        .Where(unit => unit.Order.Kind == UnitOrderKind.GatherWood && unit.Order.TargetId is { } tree && depletedNow.Contains(tree))
        .Select(unit => unit.Id));

    foreach (EntityId workerId in toIdle)
    {
      if (!units[workerId].Order.IsIdle)
      {
        units = units.SetItem(workerId, units[workerId].AsIdle());
        context.CanceledOrders.Add(workerId);
      }
    }

    return units;
  }

  /// <summary>Coherencia (06 §4, fase 6): mismo tick y toda referencia existente en el mundo. Si no, es una falla crítica.</summary>
  private static void EnsureCoherent(WorldState world, TickId tick, EconomyTickResult result)
  {
    Require(result.Tick == tick, $"Economy respondió el tick {result.Tick} en lugar de {tick}.");
    foreach (GathererState gatherer in result.GathererUpdates)
    {
      Require(
          world.Units.TryGetValue(gatherer.WorkerId, out UnitEntity? unit) && unit.Order.Kind == UnitOrderKind.GatherWood,
          $"Economy actualizó el contador de {gatherer.WorkerId}, que no está recolectando.");
    }

    foreach (EntityId workerId in result.WorkersToIdle)
    {
      Require(world.Units.ContainsKey(workerId), $"Economy pidió Idle para la unidad inexistente {workerId}.");
    }

    foreach (TreeEconomyState tree in result.TreeUpdates)
    {
      Require(world.Trees.ContainsKey(tree.TreeId), $"Economy actualizó el árbol inexistente {tree.TreeId}.");
    }

    foreach (FarmEconomyState farm in result.FarmUpdates)
    {
      Require(
          world.Buildings.TryGetValue(farm.FarmId, out BuildingEntity? building) && building.FarmEconomy is not null,
          $"Economy actualizó {farm.FarmId}, que no es una granja completada.");
    }

    foreach (PlayerResourceDelta credit in result.PlayerDeltas)
    {
      Require(world.Players.ContainsKey(credit.Player), $"Economy acreditó al jugador inexistente {credit.Player}.");
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
