using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement;

/// <summary>
/// Reglas espaciales que Simulation, Construction (vía Simulation) y Production consultan fuera del cálculo del tick.
/// Implementaciones puras y seguras para uso concurrente.
/// </summary>
public interface IMovementRules
{
    /// <summary>Distancia máxima por tick del tipo de unidad: <c>SpeedPerSecond / TickRate</c> (O-007).</summary>
    double GetDistancePerTick(UnitType unitType);

    /// <summary>Rango de interacción de una orden de tarea (GatherWood, Build, RepairFarm, Attack).</summary>
    double GetInteractionRange(UnitOrderKind taskKind);

    /// <summary>
    /// Valida una ruta candidata desde <paramref name="start"/> (posición oficial) contra el mapa actual (04 §4, contrato 04 §4).
    /// </summary>
    RouteValidation ValidateRoute(NavigationGrid map, PositionXZ start, EquatableArray<PositionXZ> waypoints);

    /// <summary>Verdadero si <paramref name="position"/> está en rango de interacción del objetivo (inclusivo; huellas al borde).</summary>
    bool IsWithinInteractionRange(GridSpec grid, PositionXZ position, MovementTarget target, double range);

    /// <summary>
    /// Búsqueda de conectividad en la grilla (D-026): ¿existe un camino de celdas libres desde la celda de
    /// <paramref name="start"/> hasta alguna celda en rango del objetivo? No genera rutas para el cliente.
    /// Para un Build se invoca con <see cref="NavigationGrid.WithBlockedFootprint"/>.
    /// </summary>
    bool CheckReachability(NavigationGrid map, PositionXZ start, MovementTarget target, double range);

    /// <summary>Evalúa una orden individual: compatibilidad de tipo, ruta válida y llegada al destino o al rango.</summary>
    UnitOrderEvaluation EvaluateUnitOrder(UnitOrderRequest request);

    /// <summary>
    /// Evalúa un RepathResponse: <paramref name="orderId"/> debe ser la orden vigente de la unidad (si no,
    /// <c>Movement.StaleOrder</c>) y la ruta debe ser válida y llegar a la misma intención (04 §5, §8).
    /// <paramref name="target"/> es la geometría actual del objetivo, o nulo si la orden es Move.
    /// </summary>
    UnitOrderEvaluation EvaluateRepathResponse(
        UnitMovementState unit,
        OrderId orderId,
        MovementTarget? target,
        EquatableArray<PositionXZ> waypoints,
        NavigationGrid map);
}
