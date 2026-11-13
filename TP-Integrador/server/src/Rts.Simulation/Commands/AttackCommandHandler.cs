using Rts.Combat;
using Rts.Combat.Model;
using Rts.Movement;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>Attack</c> (02 §6, 04 §3): un grupo de unidades ataca una unidad o un edificio. Por cada unidad, en el orden del comando:
/// <list type="number">
/// <item>Simulation: que exista y sea del jugador.</item>
/// <item>Combat (<see cref="ICombatRules.ValidateAttackTarget"/>): que la unidad sea soldado y el objetivo exista, sea unidad o edificio
/// y sea enemigo.</item>
/// <item>Movement: que la ruta termine en rango del objetivo (un punto si es unidad; la huella si es edificio).</item>
/// </list>
/// Las aceptadas reciben la orden Attack con un <c>OrderId</c> nuevo; las rechazadas conservan su orden anterior.
/// </summary>
internal sealed class AttackCommandHandler : ICommandHandler
{
  private readonly ICombatRules _combat;
  private readonly IMovementRules _movement;

  public AttackCommandHandler(ICombatRules combat, IMovementRules movement)
  {
    _combat = combat ?? throw new ArgumentNullException(nameof(combat));
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
  }

  public CommandType Type => CommandType.Attack;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var attack = (AttackCommand)command.Command;
    WorldState world = context.World;
    AttackTargetInfo? target = AttackTargetOf(world, attack.TargetId);
    var order = new TaskOrder(UnitOrderKind.Attack, attack.TargetId, world.MovementTargetOf(attack.TargetId));

    TaskOrderPlan plan = TaskOrderPlan.Evaluate(
        world, command.Player, order, attack.Routes, context.Navigation, _movement,
        attacker => _combat.ValidateAttackTarget(attacker.Owner, attacker.Type, target));

    plan.CommitTo(context);
    return plan.ToResult(command, context.Tick);
  }

  /// <summary>Lo que Combat necesita saber del objetivo: su tipo de entidad y su dueño (nulo para un árbol). Nulo si no existe.</summary>
  private static AttackTargetInfo? AttackTargetOf(WorldState world, EntityId id)
  {
    if (world.Units.TryGetValue(id, out UnitEntity? unit))
    {
      return new AttackTargetInfo(id, EntityKind.Unit, unit.Owner);
    }

    if (world.Buildings.TryGetValue(id, out BuildingEntity? building))
    {
      return new AttackTargetInfo(id, EntityKind.Building, building.Owner);
    }

    return world.Trees.ContainsKey(id) ? new AttackTargetInfo(id, EntityKind.Tree, Owner: null) : null;
  }
}
