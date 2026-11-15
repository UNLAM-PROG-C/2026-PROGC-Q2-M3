using Rts.Movement.Model;
using Rts.Shared.Geometry;

namespace Rts.Movement;

/// <summary>
/// Búsqueda en anchura (BFS) sobre celdas libres de la grilla de navegación (contrato 04 §5, D-026).
/// Verifica si existe un camino de celdas libres desde la celda de inicio hasta alguna celda libre
/// cuyo rectángulo esté dentro del rango de interacción del objetivo.
/// </summary>
internal static class ReachabilitySearch
{
  private const int OrthogonalStep = 1;
  private const int NoStep = 0;

  private static readonly (int ColDelta, int RowDelta)[] Directions =
  [
    (OrthogonalStep, NoStep),
    (-OrthogonalStep, NoStep),
    (NoStep, OrthogonalStep),
    (NoStep, -OrthogonalStep),
    (OrthogonalStep, OrthogonalStep),
    (OrthogonalStep, -OrthogonalStep),
    (-OrthogonalStep, OrthogonalStep),
    (-OrthogonalStep, -OrthogonalStep),
  ];

  public static bool IsTargetReachable(
      NavigationGrid map, PositionXZ start, MovementTarget target, double range)
  {
    if (!map.Grid.Contains(start))
    {
      return false;
    }

    GridCell startCell = map.Grid.ToCell(start);
    if (map.IsBlocked(startCell))
    {
      return false;
    }

    return RunBfs(map, startCell, target, range);
  }

  private static bool RunBfs(
      NavigationGrid map, GridCell startCell, MovementTarget target, double range)
  {
    var visited = new HashSet<GridCell> { startCell };
    var queue = new Queue<GridCell>();
    queue.Enqueue(startCell);

    while (queue.Count > 0)
    {
      GridCell current = queue.Dequeue();
      if (IsCellWithinTargetRange(map.Grid, current, target, range))
      {
        return true;
      }

      EnqueueNeighbors(map, current, visited, queue);
    }

    return false;
  }

  private static void EnqueueNeighbors(
      NavigationGrid map, GridCell current, HashSet<GridCell> visited, Queue<GridCell> queue)
  {
    foreach ((int colDelta, int rowDelta) in Directions)
    {
      if (CanStep(map, current, colDelta, rowDelta, out GridCell neighbor) && visited.Add(neighbor))
      {
        queue.Enqueue(neighbor);
      }
    }
  }

  private static bool CanStep(
      NavigationGrid map, GridCell from, int colDelta, int rowDelta, out GridCell neighbor)
  {
    neighbor = new GridCell(from.Column + colDelta, from.Row + rowDelta);
    if (map.IsBlocked(neighbor))
    {
      return false;
    }

    if (colDelta != NoStep && rowDelta != NoStep)
    {
      var orthoCol = new GridCell(from.Column + colDelta, from.Row);
      var orthoRow = new GridCell(from.Column, from.Row + rowDelta);
      return !map.IsBlocked(orthoCol) && !map.IsBlocked(orthoRow);
    }

    return true;
  }

  private static bool IsCellWithinTargetRange(
      GridSpec grid, GridCell cell, MovementTarget target, double range)
  {
    WorldRect cellRect = GetCellRect(grid, cell);
    if (target.Point is { } targetPoint)
    {
      return cellRect.IsWithinRange(targetPoint, range);
    }

    WorldRect targetRect = grid.ToWorldRect(target.Footprint!.Value);
    return RectDistanceSquared(cellRect, targetRect) <= range * range;
  }

  private static WorldRect GetCellRect(GridSpec grid, GridCell cell)
  {
    double minX = cell.Column * grid.CellSize;
    double minZ = cell.Row * grid.CellSize;
    return new WorldRect(minX, minZ, minX + grid.CellSize, minZ + grid.CellSize);
  }

  private static double RectDistanceSquared(WorldRect a, WorldRect b)
  {
    double dx = Math.Max(0.0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
    double dz = Math.Max(0.0, Math.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
    return (dx * dx) + (dz * dz);
  }
}

