using Rts.Movement.Model;
using Rts.Shared.Ids;

namespace Rts.Simulation.State;

/// <summary>Traducción de un ID de objetivo a lo que necesita Movement. La usan la fase 2 y los handlers de órdenes de tarea.</summary>
internal static class WorldTargets
{
  /// <summary>
  /// Geometría del objetivo (D-019): unidad o árbol ⇒ un punto; edificio ⇒ su huella. Nulo si la entidad no existe.
  /// </summary>
  public static MovementTarget? MovementTargetOf(this WorldState world, EntityId id)
  {
    if (world.Units.TryGetValue(id, out UnitEntity? unit))
    {
      return MovementTarget.AtPoint(id, unit.Position);
    }

    if (world.Buildings.TryGetValue(id, out BuildingEntity? building))
    {
      return MovementTarget.WithFootprint(id, building.Footprint);
    }

    return world.Trees.TryGetValue(id, out TreeEntity? tree) ? MovementTarget.AtPoint(id, tree.Position) : null;
  }
}
