using System.Collections.Immutable;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement;

/// <summary>
/// Calculador del avance físico de las unidades en el tick (fase 2 del loop, contrato 04 §5).
/// </summary>
public sealed class MovementCalculator : IMovementCalculator
{
  private const double NoBudgetRemaining = 0.0;

  private readonly IMovementRules _rules;

  public MovementCalculator(IMovementRules rules)
  {
    ArgumentNullException.ThrowIfNull(rules);
    _rules = rules;
  }

  /// <inheritdoc />
  public MovementTickResult CalculateTick(MovementTickInput input)
  {
    ArgumentNullException.ThrowIfNull(input);

    var updates = ImmutableArray.CreateBuilder<UnitMovementState>();
    var ordersEnded = ImmutableArray.CreateBuilder<OrderEnded>();
    var events = ImmutableArray.CreateBuilder<IDomainEventPayload>();

    foreach (UnitMovementState unit in input.Units.OrderBy(u => u.UnitId))
    {
      if (!unit.Order.IsIdle && !unit.IsRepathPending && unit.Order.Kind == UnitOrderKind.Move)
      {
        AdvanceMoveUnit(unit, updates, ordersEnded, events);
      }
    }

    return new MovementTickResult(
        input.Tick,
        new EquatableArray<UnitMovementState>(updates.ToImmutable()),
        new EquatableArray<OrderEnded>(ordersEnded.ToImmutable()),
        new EquatableArray<IDomainEventPayload>(events.ToImmutable()));
  }

  private void AdvanceMoveUnit(
      UnitMovementState unit,
      ImmutableArray<UnitMovementState>.Builder updates,
      ImmutableArray<OrderEnded>.Builder ordersEnded,
      ImmutableArray<IDomainEventPayload>.Builder events)
  {
    double budget = _rules.GetDistancePerTick(unit.Type);
    (PositionXZ newPosition, EquatableArray<PositionXZ> remaining) =
        AdvanceAlongWaypoints(unit.Position, unit.RemainingWaypoints, budget);

    if (remaining.IsEmpty)
    {
      FinishMoveOrder(unit, newPosition, updates, ordersEnded, events);
    }
    else
    {
      updates.Add(new UnitMovementState(
          unit.UnitId, unit.Owner, unit.Type, newPosition, unit.Order,
          remaining, unit.RouteMapRevision, unit.RepathPendingSinceTick, unit.LastPursuitRepathTick));
    }
  }

  private static (PositionXZ NewPosition, EquatableArray<PositionXZ> Remaining) AdvanceAlongWaypoints(
      PositionXZ currentPosition, EquatableArray<PositionXZ> waypoints, double budget)
  {
    int index = 0;
    while (index < waypoints.Count && budget > NoBudgetRemaining)
    {
      PositionXZ target = waypoints[index];
      double distance = currentPosition.DistanceTo(target);
      if (distance <= budget)
      {
        currentPosition = target;
        budget -= distance;
        index++;
      }
      else
      {
        currentPosition = StepTowards(currentPosition, target, distance, budget);
        budget = NoBudgetRemaining;
      }
    }

    return (currentPosition, GetRemainingWaypoints(waypoints, index));
  }

  private static PositionXZ StepTowards(
      PositionXZ from, PositionXZ to, double distanceToTarget, double stepDistance)
  {
    double ratio = stepDistance / distanceToTarget;
    return new PositionXZ(
        from.X + ((to.X - from.X) * ratio),
        from.Z + ((to.Z - from.Z) * ratio));
  }

  private static EquatableArray<PositionXZ> GetRemainingWaypoints(
      EquatableArray<PositionXZ> waypoints, int startIndex)
  {
    if (startIndex >= waypoints.Count)
    {
      return default;
    }

    var remaining = ImmutableArray.CreateBuilder<PositionXZ>(waypoints.Count - startIndex);
    for (int i = startIndex; i < waypoints.Count; i++)
    {
      remaining.Add(waypoints[i]);
    }

    return new EquatableArray<PositionXZ>(remaining.MoveToImmutable());
  }

  private static void FinishMoveOrder(
      UnitMovementState unit,
      PositionXZ newPosition,
      ImmutableArray<UnitMovementState>.Builder updates,
      ImmutableArray<OrderEnded>.Builder ordersEnded,
      ImmutableArray<IDomainEventPayload>.Builder events)
  {
    updates.Add(new UnitMovementState(
        unit.UnitId, unit.Owner, unit.Type, newPosition, MovementOrder.Idle,
        default, unit.RouteMapRevision));
    ordersEnded.Add(new OrderEnded(unit.UnitId, unit.Order.OrderId, OrderEndReason.Completed));
    events.Add(new UnitOrderChanged(unit.UnitId, OrderId.None, UnitOrderKind.Idle));
  }
}
