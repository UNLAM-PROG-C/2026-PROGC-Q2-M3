using System.Collections.Immutable;
using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>Move</c> (04 §3): cada unidad trae su ruta candidata, calculada por Godot. Por cada una, en el orden del comando:
/// <list type="number">
/// <item>Simulation verifica que exista y sea del jugador (<c>Common.EntityNotFound</c>, <c>Common.NotOwner</c>).</item>
/// <item>Movement evalúa la orden contra el mapa <b>actual</b> (<see cref="IMovementRules.EvaluateUnitOrder"/>).</item>
/// <item>Si se acepta, la unidad recibe un <c>OrderId</c> nuevo, la orden Move y la ruta validada.</item>
/// </list>
/// Una unidad rechazada conserva su orden anterior. Las aceptadas se confirman juntas y emiten
/// <see cref="UnitOrderChanged"/> en el orden del comando.
/// </summary>
internal sealed class MoveCommandHandler : ICommandHandler
{
  private readonly IMovementRules _rules;

  public MoveCommandHandler(IMovementRules rules)
  {
    ArgumentNullException.ThrowIfNull(rules);
    _rules = rules;
  }

  public CommandType Type => CommandType.Move;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var move = (MoveCommand)command.Command;
    var results = new List<EntityCommandResult>();
    var units = context.World.Units;
    OrderId lastOrderId = context.World.LastOrderId;
    var changes = new List<UnitOrderChanged>();

    foreach (UnitRouteCandidate candidate in move.Routes)
    {
      ErrorCode? ownership = GroupCommandSupport.CheckOwnedUnit(context.World, command.Player, candidate.UnitId);
      if (ownership is { } notOwned)
      {
        results.Add(EntityCommandResult.Rejected(candidate.UnitId, notOwned));
        continue;
      }

      UnitEntity unit = units[candidate.UnitId];
      UnitOrderEvaluation evaluation = Evaluate(unit, move, candidate, context);
      if (evaluation.Error is { } invalid)
      {
        results.Add(EntityCommandResult.Rejected(candidate.UnitId, invalid));
        continue;
      }

      lastOrderId = lastOrderId.Next();
      MovementOrder order = MovementOrder.Move(lastOrderId, move.Destination);
      units = units.SetItem(unit.Id, unit.WithNewOrder(order, evaluation.AcceptedRoute, context.World.MapRevision));
      results.Add(EntityCommandResult.Accepted(unit.Id));
      changes.Add(new UnitOrderChanged(unit.Id, lastOrderId, UnitOrderKind.Move));
    }

    Commit(context, units, lastOrderId, changes);
    return GroupCommandSupport.ToResult(command, context.Tick, [.. results]);
  }

  private UnitOrderEvaluation Evaluate(UnitEntity unit, MoveCommand move, UnitRouteCandidate candidate, TickContext context)
  {
    var request = new UnitOrderRequest(
        unit.ToMovementState(), UnitOrderKind.Move, move.Destination, target: null, candidate.Waypoints, context.Navigation);
    return _rules.EvaluateUnitOrder(request);
  }

  private static void Commit(
      TickContext context,
      ImmutableSortedDictionary<EntityId, UnitEntity> units,
      OrderId lastOrderId,
      List<UnitOrderChanged> changes)
  {
    if (changes.Count == 0)
    {
      return;
    }

    context.World = context.World.With(units: units, lastOrderId: lastOrderId);
    foreach (UnitOrderChanged change in changes)
    {
      context.Emit(change);
    }
  }
}
