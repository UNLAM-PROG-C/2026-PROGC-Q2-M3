using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Fake determinista de las reglas de Movement (el módulo real es de Ivan). <see cref="EvaluateUnitOrder"/> acepta
/// siempre la ruta tal como llega, salvo para las unidades cargadas en <see cref="Rejections"/>. Guarda los pedidos para
/// poder revisar qué le pasó Simulation. Los demás métodos todavía no los usa ninguna prueba.
/// </summary>
internal sealed class FakeMovementRules : IMovementRules
{
  private readonly List<UnitOrderRequest> _requests = [];

  /// <summary>Unidades que el fake rechaza, con el error que devuelve para cada una.</summary>
  public Dictionary<EntityId, ErrorCode> Rejections { get; } = [];

  public IReadOnlyList<UnitOrderRequest> Requests => _requests;

  public UnitOrderEvaluation EvaluateUnitOrder(UnitOrderRequest request)
  {
    _requests.Add(request);
    EntityId unitId = request.Unit.UnitId;
    return Rejections.TryGetValue(unitId, out ErrorCode error)
        ? UnitOrderEvaluation.Rejected(unitId, error)
        : UnitOrderEvaluation.Accepted(unitId, request.Waypoints);
  }

  /// <summary>Rango de interacción de prueba, igual para toda tarea (valor de prueba, no de balance).</summary>
  public const double TestInteractionRange = 1.5;

  public double GetDistancePerTick(UnitType unitType) => throw NotYet();

  public double GetInteractionRange(UnitOrderKind taskKind) => TestInteractionRange;

  public RouteValidation ValidateRoute(NavigationGrid map, PositionXZ start, EquatableArray<PositionXZ> waypoints) => throw NotYet();

  /// <summary>Distancia recta hasta un objetivo puntual (árbol o unidad). Las huellas todavía no las usa ninguna prueba.</summary>
  public bool IsWithinInteractionRange(GridSpec grid, PositionXZ position, MovementTarget target, double range)
  {
    PositionXZ point = target.Point ?? throw NotYet();
    double dx = point.X - position.X;
    double dz = point.Z - position.Z;
    return Math.Sqrt((dx * dx) + (dz * dz)) <= range;
  }

  public bool CheckReachability(NavigationGrid map, PositionXZ start, MovementTarget target, double range) => throw NotYet();

  public UnitOrderEvaluation EvaluateRepathResponse(
      UnitMovementState unit,
      OrderId orderId,
      MovementTarget? target,
      EquatableArray<PositionXZ> waypoints,
      NavigationGrid map) => throw NotYet();

  private static NotSupportedException NotYet() => new("Todavía no lo usa ninguna prueba de Simulation.");
}
