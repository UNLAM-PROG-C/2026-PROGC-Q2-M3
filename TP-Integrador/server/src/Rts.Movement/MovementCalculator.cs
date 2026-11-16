using System.Collections.Immutable;
using Rts.Movement.Configuration;
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
  private readonly MovementConfig? _config;

  public MovementCalculator(IMovementRules rules, MovementConfig? config = null)
  {
    ArgumentNullException.ThrowIfNull(rules);
    config?.Validate();
    _rules = rules;
    _config = config;
  }

  /// <inheritdoc />
  public MovementTickResult CalculateTick(MovementTickInput input)
  {
    ArgumentNullException.ThrowIfNull(input);

    var context = new TickContext(
        input.Tick,
        input.Map,
        input.Targets.ToDictionary(t => t.Id),
        ImmutableArray.CreateBuilder<UnitMovementState>(),
        ImmutableArray.CreateBuilder<OrderEnded>(),
        ImmutableArray.CreateBuilder<IDomainEventPayload>());

    foreach (UnitMovementState unit in input.Units.OrderBy(u => u.UnitId))
    {
      if (!unit.Order.IsIdle)
      {
        ProcessActiveUnit(unit, context);
      }
    }

    return context.ToResult();
  }

  private void ProcessActiveUnit(UnitMovementState unit, TickContext ctx)
  {
    if (unit.IsRepathPending)
    {
      HandleRepathPendingUnit(unit, ctx);
      return;
    }

    if (!TryRevalidateMapRevision(unit, ctx, out UnitMovementState currentUnit))
    {
      return;
    }

    if (currentUnit.Order.Kind == UnitOrderKind.Move)
    {
      AdvanceMoveUnit(currentUnit, ctx, currentUnit != unit);
    }
    else
    {
      ProcessTaskUnit(currentUnit, ctx, currentUnit != unit);
    }
  }

  private void HandleRepathPendingUnit(UnitMovementState unit, TickContext ctx)
  {
    if (_config is not null
        && ctx.Tick.Value - unit.RepathPendingSinceTick!.Value.Value >= _config.RepathTimeoutTicks)
    {
      EndUnitOrder(unit, unit.Position, OrderEndReason.RepathTimeout, ctx);
    }
  }

  private bool TryRevalidateMapRevision(
      UnitMovementState unit, TickContext ctx, out UnitMovementState updatedUnit)
  {
    updatedUnit = unit;
    if (unit.RouteMapRevision.Value >= ctx.Map.Revision.Value || unit.RemainingWaypoints.IsEmpty)
    {
      return true;
    }

    RouteValidation validation = _rules.ValidateRoute(ctx.Map, unit.Position, unit.RemainingWaypoints);
    if (validation.IsValid)
    {
      updatedUnit = CopyUnit(unit, routeMapRevision: ctx.Map.Revision);
      return true;
    }

    PauseForRepath(unit, ctx);
    return false;
  }

  private static void PauseForRepath(UnitMovementState unit, TickContext ctx)
  {
    ctx.Updates.Add(CopyUnit(unit, repathPendingSinceTick: ctx.Tick));
    ctx.Events.Add(new RepathRequested(
        unit.UnitId,
        unit.Owner,
        unit.Order.OrderId,
        ctx.Map.Revision,
        unit.Order.Destination,
        unit.Order.TargetId,
        isPaused: true));
  }

  private void ProcessTaskUnit(UnitMovementState unit, TickContext ctx, bool revisionChanged)
  {
    if (!ctx.Targets.TryGetValue(unit.Order.TargetId!.Value, out MovementTarget? target))
    {
      EndUnitOrder(unit, unit.Position, OrderEndReason.TargetLost, ctx);
      return;
    }

    double range = _rules.GetInteractionRange(unit.Order.Kind);
    if (_rules.IsWithinInteractionRange(ctx.Map.Grid, unit.Position, target, range))
    {
      HoldPositionInRange(unit, ctx, revisionChanged);
      return;
    }

    AdvanceTaskTowardsTarget(unit, target, range, ctx);
  }

  private static void HoldPositionInRange(
      UnitMovementState unit, TickContext ctx, bool revisionChanged)
  {
    if (!unit.RemainingWaypoints.IsEmpty)
    {
      ctx.Updates.Add(CopyUnit(unit, remainingWaypoints: default));
    }
    else if (revisionChanged)
    {
      ctx.Updates.Add(unit);
    }
  }

  private void AdvanceTaskTowardsTarget(
      UnitMovementState unit, MovementTarget target, double range, TickContext ctx)
  {
    if (unit.Order.Kind == UnitOrderKind.Attack && target.Point is not null)
    {
      ProcessMobilePursuit(unit, target, range, ctx);
      return;
    }

    AdvanceStaticTaskUnit(unit, target, range, ctx);
  }

  private void ProcessMobilePursuit(
      UnitMovementState unit, MovementTarget target, double range, TickContext ctx)
  {
    PositionXZ targetPos = target.Point!.Value;
    if (_rules.ValidateRoute(ctx.Map, unit.Position, [targetPos]).IsValid)
    {
      UnitMovementState directUnit = CopyUnit(
          unit, remainingWaypoints: [targetPos], routeMapRevision: ctx.Map.Revision);
      StepTaskUnit(directUnit, target, range, ctx);
      return;
    }

    ProcessObstructedPursuit(unit, target, range, ctx);
  }

  private void ProcessObstructedPursuit(
      UnitMovementState unit, MovementTarget target, double range, TickContext ctx)
  {
    if (!_rules.CheckReachability(ctx.Map, unit.Position, target, range))
    {
      EndUnitOrder(unit, unit.Position, OrderEndReason.Unreachable, ctx);
      return;
    }

    UnitMovementState current = MaybeRequestPursuitRepath(unit, target, range, ctx);
    StepTaskUnit(current, target, range, ctx, allowEmptyRoute: true);
  }

  private UnitMovementState MaybeRequestPursuitRepath(
      UnitMovementState unit, MovementTarget target, double range, TickContext ctx)
  {
    PositionXZ routeEnd = unit.RemainingWaypoints.IsEmpty ? unit.Position : unit.RemainingWaypoints[^1];
    bool needsRepath = unit.RemainingWaypoints.IsEmpty
        || !_rules.IsWithinInteractionRange(ctx.Map.Grid, routeEnd, target, range);

    if (!needsRepath || !CanEmitPursuitRepath(unit, ctx.Tick))
    {
      return unit;
    }

    ctx.Events.Add(new RepathRequested(
        unit.UnitId, unit.Owner, unit.Order.OrderId, ctx.Map.Revision,
        null, unit.Order.TargetId, isPaused: false));
    return CopyUnit(unit, lastPursuitRepathTick: ctx.Tick);
  }

  private bool CanEmitPursuitRepath(UnitMovementState unit, TickId currentTick)
  {
    if (unit.LastPursuitRepathTick is not { } lastTick)
    {
      return true;
    }

    int interval = _config?.PursuitRepathIntervalTicks ?? 1;
    return currentTick.Value - lastTick.Value >= interval;
  }

  private void AdvanceStaticTaskUnit(
      UnitMovementState unit, MovementTarget target, double range, TickContext ctx)
  {
    if (unit.RemainingWaypoints.IsEmpty)
    {
      EndUnitOrder(unit, unit.Position, OrderEndReason.Unreachable, ctx);
      return;
    }

    StepTaskUnit(unit, target, range, ctx);
  }

  private void StepTaskUnit(
      UnitMovementState unit,
      MovementTarget target,
      double range,
      TickContext ctx,
      bool allowEmptyRoute = false)
  {
    double budget = _rules.GetDistancePerTick(unit.Type);
    (PositionXZ newPos, EquatableArray<PositionXZ> remaining) =
        AdvanceAlongWaypoints(unit.Position, unit.RemainingWaypoints, budget);

    bool inRange = _rules.IsWithinInteractionRange(ctx.Map.Grid, newPos, target, range);
    if (!inRange && remaining.IsEmpty && !allowEmptyRoute)
    {
      EndUnitOrder(unit, newPos, OrderEndReason.Unreachable, ctx);
      return;
    }

    EquatableArray<PositionXZ> finalRemaining = inRange ? default : remaining;
    UnitMovementState nextState = CopyUnit(unit, position: newPos, remainingWaypoints: finalRemaining);

    if (nextState != unit || unit.LastPursuitRepathTick == ctx.Tick)
    {
      ctx.Updates.Add(nextState);
    }
  }

  private void AdvanceMoveUnit(UnitMovementState unit, TickContext ctx, bool forceUpdate)
  {
    double budget = _rules.GetDistancePerTick(unit.Type);
    (PositionXZ newPosition, EquatableArray<PositionXZ> remaining) =
        AdvanceAlongWaypoints(unit.Position, unit.RemainingWaypoints, budget);

    if (remaining.IsEmpty)
    {
      EndUnitOrder(unit, newPosition, OrderEndReason.Completed, ctx);
    }
    else if (forceUpdate || newPosition != unit.Position || remaining != unit.RemainingWaypoints)
    {
      ctx.Updates.Add(CopyUnit(unit, position: newPosition, remainingWaypoints: remaining));
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

  private static void EndUnitOrder(
      UnitMovementState unit, PositionXZ finalPosition, OrderEndReason reason, TickContext ctx)
  {
    ctx.Updates.Add(new UnitMovementState(
        unit.UnitId, unit.Owner, unit.Type, finalPosition, MovementOrder.Idle,
        default, ctx.Map.Revision));
    ctx.OrdersEnded.Add(new OrderEnded(unit.UnitId, unit.Order.OrderId, reason));
    ctx.Events.Add(new UnitOrderChanged(unit.UnitId, OrderId.None, UnitOrderKind.Idle));
  }

  private static UnitMovementState CopyUnit(
      UnitMovementState unit,
      PositionXZ? position = null,
      EquatableArray<PositionXZ>? remainingWaypoints = null,
      MapRevision? routeMapRevision = null,
      TickId? repathPendingSinceTick = null,
      TickId? lastPursuitRepathTick = null) =>
      new(
          unit.UnitId,
          unit.Owner,
          unit.Type,
          position ?? unit.Position,
          unit.Order,
          remainingWaypoints ?? unit.RemainingWaypoints,
          routeMapRevision ?? unit.RouteMapRevision,
          repathPendingSinceTick ?? unit.RepathPendingSinceTick,
          lastPursuitRepathTick ?? unit.LastPursuitRepathTick);

  private sealed record TickContext(
      TickId Tick,
      NavigationGrid Map,
      Dictionary<EntityId, MovementTarget> Targets,
      ImmutableArray<UnitMovementState>.Builder Updates,
      ImmutableArray<OrderEnded>.Builder OrdersEnded,
      ImmutableArray<IDomainEventPayload>.Builder Events)
  {
    public MovementTickResult ToResult() => new(
        Tick,
        new EquatableArray<UnitMovementState>(Updates.ToImmutable()),
        new EquatableArray<OrderEnded>(OrdersEnded.ToImmutable()),
        new EquatableArray<IDomainEventPayload>(Events.ToImmutable()));
  }
}
