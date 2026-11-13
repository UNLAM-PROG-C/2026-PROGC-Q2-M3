using System.Collections.Immutable;
using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>La orden de tarea que se quiere dar: tipo, ID del objetivo y su geometría para Movement (nula si no existe).</summary>
internal sealed record TaskOrder(UnitOrderKind Kind, EntityId TargetId, MovementTarget? Target);

/// <summary>
/// Evaluación de una orden de tarea (Attack, GatherWood, Build, RepairFarm) para un grupo de unidades (04 §3). <b>No toca el mundo</b>:
/// el handler decide si confirma con <see cref="CommitTo"/>, así puede cobrar o rechazar todo antes. Por cada unidad, en el orden del
/// comando:
/// <list type="number">
/// <item>Simulation: que exista y sea del jugador.</item>
/// <item>La validación del módulo dueño del objetivo, si la hay (por ejemplo, Combat para un Attack).</item>
/// <item>Movement: que la ruta sea válida contra el mapa recibido y termine en rango del objetivo.</item>
/// <item>Si pasa todo: <c>OrderId</c> nuevo, la orden de tarea y la ruta validada.</item>
/// </list>
/// Si una unidad falla un paso, no se hacen los siguientes y conserva su orden anterior.
/// </summary>
internal sealed class TaskOrderPlan
{
  private readonly List<EntityCommandResult> _results = [];
  private readonly List<UnitOrderChanged> _changes = [];
  private ImmutableSortedDictionary<EntityId, UnitEntity> _units;
  private OrderId _lastOrderId;

  private TaskOrderPlan(WorldState world)
  {
    _units = world.Units;
    _lastOrderId = world.LastOrderId;
  }

  /// <summary>Si al menos una unidad recibió la orden.</summary>
  public bool AnyAccepted => _changes.Count > 0;

  /// <param name="map">Mapa contra el que Movement valida las rutas: el actual, o uno hipotético (Build colocar).</param>
  /// <param name="domainCheck">Validación del módulo dueño del objetivo para cada unidad; nulo si pasa.</param>
  public static TaskOrderPlan Evaluate(
      WorldState world,
      PlayerId player,
      TaskOrder order,
      EquatableArray<UnitRouteCandidate> routes,
      NavigationGrid map,
      IMovementRules movement,
      Func<UnitEntity, ErrorCode?> domainCheck)
  {
    var plan = new TaskOrderPlan(world);
    foreach (UnitRouteCandidate candidate in routes)
    {
      ErrorCode? error = GroupCommandSupport.CheckOwnedUnit(world, player, candidate.UnitId)
          ?? domainCheck(world.Units[candidate.UnitId])
          ?? plan.TryAssign(world, order, candidate, map, movement);
      if (error is { } rejection)
      {
        plan._results.Add(EntityCommandResult.Rejected(candidate.UnitId, rejection));
      }
    }

    return plan;
  }

  /// <summary>Confirma las órdenes aceptadas como una sola operación y emite sus <see cref="UnitOrderChanged"/> en el orden del comando.</summary>
  public void CommitTo(TickContext context)
  {
    if (!AnyAccepted)
    {
      return;
    }

    context.World = context.World.With(units: _units, lastOrderId: _lastOrderId);
    foreach (UnitOrderChanged change in _changes)
    {
      context.Emit(change);
    }
  }

  public CommandResult ToResult(CommandEnvelope command, TickId tick) => GroupCommandSupport.ToResult(command, tick, [.. _results]);

  /// <summary>Paso 3 y 4: Movement evalúa la ruta; si la acepta, la unidad recibe la orden. Devuelve el error de Movement, o nulo.</summary>
  private ErrorCode? TryAssign(WorldState world, TaskOrder order, UnitRouteCandidate candidate, NavigationGrid map, IMovementRules movement)
  {
    MovementTarget target = order.Target
        ?? throw new InvalidOperationException($"Se aceptó una orden {order.Kind} sobre {order.TargetId}, que no existe.");
    UnitEntity unit = world.Units[candidate.UnitId];
    var request = new UnitOrderRequest(unit.ToMovementState(), order.Kind, destination: null, target, candidate.Waypoints, map);
    UnitOrderEvaluation evaluation = movement.EvaluateUnitOrder(request);
    if (evaluation.Error is { } invalid)
    {
      return invalid;
    }

    _lastOrderId = _lastOrderId.Next();
    MovementOrder task = MovementOrder.Task(_lastOrderId, order.Kind, order.TargetId);
    _units = _units.SetItem(unit.Id, unit.WithNewOrder(task, evaluation.AcceptedRoute, world.MapRevision));
    _results.Add(EntityCommandResult.Accepted(unit.Id));
    _changes.Add(new UnitOrderChanged(unit.Id, _lastOrderId, order.Kind));
    return null;
  }
}
