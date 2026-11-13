using Rts.Economy;
using Rts.Economy.Model;
using Rts.Movement;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>GatherWood</c> (04 §3, 01 §3): un grupo de trabajadores va a recolectar un árbol. Por cada unidad, en el orden del comando:
/// <list type="number">
/// <item>Simulation: que exista y sea del jugador.</item>
/// <item>Economy (<see cref="IEconomyRules.EvaluateGatherTarget"/>): que el objetivo sea un árbol recolectable (RECOMENDADO D-092).</item>
/// <item>Movement: que la unidad pueda recolectar (solo trabajadores) y que la ruta termine en rango del árbol.</item>
/// </list>
/// Las aceptadas reciben la orden GatherWood con un <c>OrderId</c> nuevo y el contador de recolección en 0 (01 §3.8).
/// </summary>
internal sealed class GatherWoodCommandHandler : ICommandHandler
{
  private readonly IEconomyRules _economy;
  private readonly IMovementRules _movement;

  public GatherWoodCommandHandler(IEconomyRules economy, IMovementRules movement)
  {
    _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
  }

  public CommandType Type => CommandType.GatherWood;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var gather = (GatherWoodCommand)command.Command;
    WorldState world = context.World;
    GatherTargetInfo? target = GatherTargetOf(world, gather.TreeId);
    var order = new TaskOrder(UnitOrderKind.GatherWood, gather.TreeId, world.MovementTargetOf(gather.TreeId));

    TaskOrderPlan plan = TaskOrderPlan.Evaluate(
        world, command.Player, order, gather.Routes, context.Navigation, _movement,
        _ => _economy.EvaluateGatherTarget(target)); // El objetivo es el mismo para todos: no depende del trabajador.

    plan.CommitTo(context);
    return plan.ToResult(command, context.Tick);
  }

  /// <summary>Lo que Economy necesita saber del objetivo: qué es y, si es un árbol, su estado. Nulo si no existe.</summary>
  private static GatherTargetInfo? GatherTargetOf(WorldState world, EntityId id)
  {
    if (world.Trees.TryGetValue(id, out TreeEntity? tree))
    {
      return new GatherTargetInfo(id, EntityKind.Tree, tree.Economy.Status);
    }

    if (world.Units.ContainsKey(id))
    {
      return new GatherTargetInfo(id, EntityKind.Unit, treeStatus: null);
    }

    return world.Buildings.ContainsKey(id) ? new GatherTargetInfo(id, EntityKind.Building, treeStatus: null) : null;
  }
}
