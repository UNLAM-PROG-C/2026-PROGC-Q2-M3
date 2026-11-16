using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Fake determinista del avance de Movement (el real es de Ivan): cada unidad con ruta <b>salta al siguiente waypoint</b>
/// en cada tick, sin velocidad ni colisiones. Si un Move consume su último waypoint, la orden termina (<c>Completed</c>) y la
/// unidad queda Idle. Las tareas, el repath y la persecución no se simulan. Guarda las entradas para revisarlas.
/// </summary>
internal sealed class FakeMovementCalculator : IMovementCalculator
{
  private readonly List<MovementTickInput> _inputs = [];

  public IReadOnlyList<MovementTickInput> Inputs => _inputs;

  public MovementTickResult CalculateTick(MovementTickInput input)
  {
    _inputs.Add(input);
    var updates = new List<UnitMovementState>();
    var ended = new List<OrderEnded>();
    var events = new List<IDomainEventPayload>();

    foreach (UnitMovementState unit in input.Units.OrderBy(unit => unit.UnitId))
    {
      if (unit.RemainingWaypoints.IsEmpty)
      {
        continue;
      }

      PositionXZ next = unit.RemainingWaypoints[0];
      EquatableArray<PositionXZ> rest = [.. unit.RemainingWaypoints.Skip(1)];
      bool arrived = rest.IsEmpty && unit.Order.Kind == UnitOrderKind.Move;
      MovementOrder order = arrived ? MovementOrder.Idle : unit.Order;
      updates.Add(new UnitMovementState(unit.UnitId, unit.Owner, unit.Type, next, order, rest, unit.RouteMapRevision));

      if (arrived)
      {
        ended.Add(new OrderEnded(unit.UnitId, unit.Order.OrderId, OrderEndReason.Completed));
        events.Add(new UnitOrderChanged(unit.UnitId, OrderId.None, UnitOrderKind.Idle));
      }
    }

    return new MovementTickResult(input.Tick, [.. updates], [.. ended], [.. events]);
  }
}
