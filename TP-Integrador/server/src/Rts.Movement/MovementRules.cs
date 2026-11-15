using Rts.Movement.Configuration;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Time;

namespace Rts.Movement;

/// <summary>
/// Implementación de las reglas espaciales y de validación de órdenes de movimiento (contrato 04).
/// </summary>
public sealed class MovementRules : IMovementRules
{
  private readonly MovementConfig _config;
  private readonly TickRate _tickRate;

  public MovementRules(MovementConfig config, TickRate tickRate)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(tickRate);
    config.Validate();
    _config = config;
    _tickRate = tickRate;
  }

  /// <inheritdoc />
  public double GetDistancePerTick(UnitType unitType) =>
      _tickRate.PerTick(_config.SpeedPerSecond[unitType]);

  /// <inheritdoc />
  public double GetInteractionRange(UnitOrderKind taskKind) => taskKind switch
  {
    UnitOrderKind.GatherWood => _config.GatherRange,
    UnitOrderKind.Build or UnitOrderKind.RepairFarm => _config.BuildRange,
    UnitOrderKind.Attack => _config.AttackRange,
    _ => throw new ArgumentOutOfRangeException(
        nameof(taskKind), taskKind, "Orden sin rango de interacción."),
  };

  /// <inheritdoc />
  public bool IsWithinInteractionRange(
      GridSpec grid, PositionXZ position, MovementTarget target, double range)
  {
    ArgumentNullException.ThrowIfNull(grid);
    ArgumentNullException.ThrowIfNull(target);
    return target.Point is { } targetPoint
        ? position.IsWithinRange(targetPoint, range)
        : grid.ToWorldRect(target.Footprint!.Value).IsWithinRange(position, range);
  }

  /// <inheritdoc />
  public RouteValidation ValidateRoute(
      NavigationGrid map, PositionXZ start, EquatableArray<PositionXZ> waypoints)
  {
    ArgumentNullException.ThrowIfNull(map);
    PositionXZ current = start;
    for (int i = 0; i < waypoints.Count; i++)
    {
      PositionXZ next = waypoints[i];
      if (!map.Grid.Contains(next) || SegmentGridValidator.TouchesBlockedCell(map, current, next))
      {
        return RouteValidation.Invalid(MovementErrors.InvalidPath, failedSegmentIndex: i);
      }
      current = next;
    }

    return RouteValidation.Valid;
  }

  /// <inheritdoc />
  public UnitOrderEvaluation EvaluateUnitOrder(UnitOrderRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (request.Kind == UnitOrderKind.Idle)
    {
      return UnitOrderEvaluation.Accepted(request.Unit.UnitId, default);
    }

    ErrorCode? error =
        CheckCompatibility(request)
        ?? CheckOrderBoundsAndLimits(request)
        ?? ValidateRoute(request.Map, request.Unit.Position, request.Waypoints).Error
        ?? CheckGoalReached(request);

    return error is { } code
        ? UnitOrderEvaluation.Rejected(request.Unit.UnitId, code)
        : UnitOrderEvaluation.Accepted(request.Unit.UnitId, request.Waypoints);
  }

  /// <inheritdoc />
  public bool CheckReachability(
      NavigationGrid map, PositionXZ start, MovementTarget target, double range)
  {
    ArgumentNullException.ThrowIfNull(map);
    ArgumentNullException.ThrowIfNull(target);
    return true;
  }

  /// <inheritdoc />
  public UnitOrderEvaluation EvaluateRepathResponse(
      UnitMovementState unit,
      OrderId orderId,
      MovementTarget? target,
      EquatableArray<PositionXZ> waypoints,
      NavigationGrid map)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(map);
    if (unit.Order.OrderId != orderId)
    {
      return UnitOrderEvaluation.Rejected(unit.UnitId, MovementErrors.StaleOrder);
    }

    RouteValidation validation = ValidateRoute(map, unit.Position, waypoints);
    return validation.Error is { } error
        ? UnitOrderEvaluation.Rejected(unit.UnitId, error)
        : UnitOrderEvaluation.Accepted(unit.UnitId, waypoints);
  }

  private static ErrorCode? CheckCompatibility(UnitOrderRequest request) =>
      UnitOrderCompatibility.IsAllowed(request.Unit.Type, request.Kind)
          ? null
          : CommonErrors.IncompatibleEntity;

  private ErrorCode? CheckOrderBoundsAndLimits(UnitOrderRequest request)
  {
    if (request.Destination is { } dest && !request.Map.Grid.Contains(dest))
    {
      return MovementErrors.DestinationOutOfBounds;
    }

    return request.Waypoints.Count > _config.MaxWaypointsPerRoute
        ? MovementErrors.TooManyWaypoints
        : null;
  }

  private ErrorCode? CheckGoalReached(UnitOrderRequest request)
  {
    PositionXZ routeEnd = request.Waypoints.IsEmpty
        ? request.Unit.Position
        : request.Waypoints[^1];

    bool reached = request.Kind == UnitOrderKind.Move
        ? routeEnd.IsWithinRange(request.Destination!.Value, _config.ArrivalTolerance)
        : IsWithinInteractionRange(
            request.Map.Grid, routeEnd, request.Target!, GetInteractionRange(request.Kind));

    return reached ? null : MovementErrors.RouteDoesNotReachGoal;
  }
}
