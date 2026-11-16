using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>RepathResponse</c> (04 §3, D-028): la ruta nueva que manda Godot después de un <c>RepathRequested</c>. A diferencia de las otras
/// órdenes, <b>no crea una orden</b>: le cambia la ruta a la orden vigente. Es de una sola unidad, así que el resultado no es grupal.
/// <list type="number">
/// <item>Simulation: que la unidad exista y sea del jugador.</item>
/// <item>Movement (<see cref="IMovementRules.EvaluateRepathResponse"/>): que el <c>OrderId</c> sea el de la orden vigente (si no,
/// <c>Movement.StaleOrder</c>) y que la ruta sea válida contra el mapa actual y llegue a la misma intención.</item>
/// <item>Si se acepta: la unidad conserva su orden y su <c>OrderId</c>, recibe la ruta y se limpia el repath pendiente.</item>
/// </list>
/// No emite eventos: la orden no cambió. Una respuesta tardía nunca revive una orden cancelada o reemplazada (04 §8).
/// </summary>
internal sealed class RepathResponseCommandHandler : ICommandHandler
{
  private readonly IMovementRules _movement;

  public RepathResponseCommandHandler(IMovementRules movement)
  {
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
  }

  public CommandType Type => CommandType.RepathResponse;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var repath = (RepathResponseCommand)command.Command;
    WorldState world = context.World;
    ErrorCode? ownership = GroupCommandSupport.CheckOwnedUnit(world, command.Player, repath.UnitId);
    if (ownership is { } notOwned)
    {
      return CommandResult.Rejected(command, context.Tick, notOwned);
    }

    UnitEntity unit = world.Units[repath.UnitId];
    UnitOrderEvaluation evaluation = _movement.EvaluateRepathResponse(
        unit.ToMovementState(), repath.OrderId, CurrentTargetOf(world, unit), repath.Waypoints, context.Navigation);
    if (evaluation.Error is { } invalid)
    {
      return CommandResult.Rejected(command, context.Tick, invalid);
    }

    UnitEntity repathed = unit.WithRepathedRoute(evaluation.AcceptedRoute, world.MapRevision);
    context.World = world.With(units: world.Units.SetItem(unit.Id, repathed));
    return CommandResult.Accepted(command, context.Tick);
  }

  /// <summary>
  /// Geometría del objetivo de la orden <b>vigente</b>: nula si es Move o Idle. Una orden de tarea siempre apunta a algo que existe,
  /// porque al destruir una entidad se cancelan las órdenes que la apuntaban (D-025); si no, el mundo es incoherente.
  /// </summary>
  private static MovementTarget? CurrentTargetOf(WorldState world, UnitEntity unit)
  {
    if (unit.Order.TargetId is not { } targetId)
    {
      return null;
    }

    return world.MovementTargetOf(targetId)
        ?? throw new InvalidOperationException($"La unidad {unit.Id} tiene una orden {unit.Order.Kind} sobre {targetId}, que no existe.");
  }
}
