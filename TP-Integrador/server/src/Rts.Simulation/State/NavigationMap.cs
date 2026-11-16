using Rts.Movement.Model;
using Rts.Shared.Geometry;

namespace Rts.Simulation.State;

/// <summary>
/// Arma la vista de navegación de Movement a partir del mundo oficial (04 §4): bloquean las huellas de todos los
/// edificios y vallas, en obra o completados. Árboles y unidades no bloquean.
/// </summary>
internal static class NavigationMap
{
  public static NavigationGrid From(WorldState world)
  {
    ArgumentNullException.ThrowIfNull(world);
    IEnumerable<GridCell> blocked = world.Buildings.Values.SelectMany(building => building.Footprint.Cells());
    return new NavigationGrid(world.Grid, world.MapRevision, blocked);
  }
}
