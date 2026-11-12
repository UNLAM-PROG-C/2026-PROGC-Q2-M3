using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Movement;
using Rts.Movement.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>
/// Fase 3 (06 §4, contrato 03 §5-6). Corre en el hilo de Simulation, antes del snapshot: Economy y Combat ya ven las obras de este tick.
/// <list type="number">
/// <item>Arma la entrada de Construction: las obras, las granjas en reparación y <b>solo</b> los constructores y reparadores
/// <b>en rango</b> (lo decide Movement con la huella del edificio).</item>
/// <item>Llama al calculador de Construction (función pura).</item>
/// <item>Verifica la coherencia y aplica: HP de las obras, obras terminadas (operativas desde N+1 con el estado de su tipo),
/// progreso de reparaciones y reparaciones terminadas.</item>
/// <item>Cascada: los trabajadores de lo que se terminó pasan a Idle, también los que no estaban en rango. Sus
/// <c>UnitOrderChanged</c> van después de los eventos de Construction, por <c>UnitId</c> (06 §5).</item>
/// </list>
/// </summary>
internal sealed class ConstructionPhase : ITickPhase
{
  private readonly IConstructionCalculator _calculator;
  private readonly IConstructionRules _rules;
  private readonly IMovementRules _movement;
  private readonly int _batchesPerFarmCycle;

  public ConstructionPhase(
      IConstructionCalculator calculator,
      IConstructionRules rules,
      IMovementRules movement,
      int batchesPerFarmCycle)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchesPerFarmCycle);
    _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
    _batchesPerFarmCycle = batchesPerFarmCycle;
  }

  public SimulationPhase Phase => SimulationPhase.Construction;

  public void Execute(TickContext context)
  {
    WorldState world = context.World;
    var input = new ConstructionTickInput(
        context.Tick,
        [.. world.Buildings.Values
            .Where(building => !building.IsCompleted)
            .Select(site => new BuildingUnderConstruction(site.Id, site.Owner, site.Type, site.Hp))],
        [.. WorkersInRange(world, UnitOrderKind.Build, building => !building.IsCompleted)],
        [.. world.Buildings.Values.Select(building => building.Repair).OfType<FarmRepairState>()],
        [.. WorkersInRange(world, UnitOrderKind.RepairFarm, building => building.Repair is not null)]);

    ConstructionTickResult result = _calculator.CalculateTick(input);
    EnsureCoherent(world, context.Tick, result);
    Apply(context, result);
  }

  private void Apply(TickContext context, ConstructionTickResult result)
  {
    WorldState world = context.World;
    TickId operationalFrom = context.Tick.Next();
    var buildings = world.Buildings;
    var finished = new HashSet<EntityId>();

    foreach (BuildingProgress progress in result.BuildingProgress)
    {
      BuildingEntity site = buildings[progress.BuildingId].WithHp(progress.Hp);
      buildings = buildings.SetItem(site.Id, progress.IsCompleted ? site.AsCompleted(operationalFrom, _batchesPerFarmCycle) : site);
      AddIf(finished, progress.IsCompleted, site.Id);
    }

    foreach (RepairProgress progress in result.RepairProgress)
    {
      BuildingEntity farm = buildings[progress.FarmId];
      buildings = buildings.SetItem(farm.Id, progress.IsCompleted
          ? farm.AsRepaired(operationalFrom, _batchesPerFarmCycle)
          : farm.WithRepair(new FarmRepairState(farm.Id, farm.Owner, progress.WorkDone)));
      AddIf(finished, progress.IsCompleted, farm.Id);
    }

    // Todos los que trabajaban en algo terminado, en rango o no (03 §6). Los diccionarios están ordenados: salen por UnitId.
    UnitEntity[] released = [.. world.Units.Values.Where(unit =>
        unit.Order.Kind is UnitOrderKind.Build or UnitOrderKind.RepairFarm
        && unit.Order.TargetId is { } target && finished.Contains(target))];
    var units = world.Units;
    foreach (UnitEntity worker in released)
    {
      units = units.SetItem(worker.Id, worker.AsIdle());
    }

    context.World = world.With(buildings: buildings, units: units);
    foreach (IDomainEventPayload proposed in result.ProposedEvents)
    {
      context.Emit(proposed);
    }

    foreach (UnitEntity worker in released)
    {
      context.Emit(new UnitOrderChanged(worker.Id, OrderId.None, UnitOrderKind.Idle));
    }
  }

  /// <summary>Trabajadores con la orden indicada sobre un edificio elegible y en rango de su huella, según Movement.</summary>
  private IEnumerable<WorkerContribution> WorkersInRange(WorldState world, UnitOrderKind kind, Func<BuildingEntity, bool> isEligible)
  {
    double range = _movement.GetInteractionRange(kind);
    foreach (UnitEntity unit in world.Units.Values)
    {
      if (unit.Order.Kind != kind
          || unit.Order.TargetId is not { } targetId
          || !world.Buildings.TryGetValue(targetId, out BuildingEntity? building)
          || !isEligible(building))
      {
        continue;
      }

      MovementTarget target = MovementTarget.WithFootprint(building.Id, building.Footprint);
      if (_movement.IsWithinInteractionRange(world.Grid, unit.Position, target, range))
      {
        yield return new WorkerContribution(unit.Id, building.Id);
      }
    }
  }

  /// <summary>
  /// Coherencia: mismo tick; cada obra existe, sigue en obra y su HP queda en (0, máximo], con "terminada" si y solo si llegó al
  /// máximo (03 §5); cada reparación es de una granja en reparación.
  /// </summary>
  private void EnsureCoherent(WorldState world, TickId tick, ConstructionTickResult result)
  {
    Require(result.Tick == tick, $"Construction respondió el tick {result.Tick} en lugar de {tick}.");
    foreach (BuildingProgress progress in result.BuildingProgress)
    {
      bool isSite = world.Buildings.TryGetValue(progress.BuildingId, out BuildingEntity? site) && !site.IsCompleted;
      Require(isSite, $"Construction avanzó {progress.BuildingId}, que no es una obra.");
      int maxHp = _rules.GetMaxHp(site!.Type);
      Require(
          progress.Hp > 0 && progress.Hp <= maxHp && progress.IsCompleted == (progress.Hp == maxHp),
          $"Construction propuso HP {progress.Hp} (terminada: {progress.IsCompleted}) para {site.Id}, con máximo {maxHp}.");
    }

    foreach (RepairProgress progress in result.RepairProgress)
    {
      Require(
          world.Buildings.TryGetValue(progress.FarmId, out BuildingEntity? farm) && farm.Repair is not null,
          $"Construction avanzó la reparación de {progress.FarmId}, que no está en reparación.");
    }
  }

  private static void AddIf(HashSet<EntityId> set, bool condition, EntityId id)
  {
    if (condition)
    {
      set.Add(id);
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
