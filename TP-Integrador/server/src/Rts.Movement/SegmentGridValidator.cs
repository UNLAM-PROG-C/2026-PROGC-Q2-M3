using Rts.Movement.Model;
using Rts.Shared.Geometry;

namespace Rts.Movement;

/// <summary>
/// Validador geométrico de segmentos contra la grilla de navegación (contrato 04 §4).
/// Verifica si un segmento continuo A -> B toca alguna celda bloqueada,
/// incluyendo sus bordes y esquinas.
/// </summary>
internal static class SegmentGridValidator
{
  private const double MinRatio = 0.0;
  private const double MaxRatio = 1.0;
  private const double Epsilon = 1e-9;
  private const int NeighborCellMargin = 1;

  public static bool TouchesBlockedCell(NavigationGrid map, PositionXZ start, PositionXZ end)
  {
    (int minCol, int maxCol, int minRow, int maxRow) = GetBoundingCellRange(map.Grid, start, end);
    for (int col = minCol; col <= maxCol; col++)
    {
      for (int row = minRow; row <= maxRow; row++)
      {
        var cell = new GridCell(col, row);
        if (map.IsBlocked(cell) && SegmentTouchesCell(map.Grid, cell, start, end))
        {
          return true;
        }
      }
    }

    return false;
  }

  private static (int MinCol, int MaxCol, int MinRow, int MaxRow) GetBoundingCellRange(
      GridSpec grid, PositionXZ start, PositionXZ end)
  {
    GridCell startCell = grid.ToCell(start);
    GridCell endCell = grid.ToCell(end);
    int minCol = Math.Min(startCell.Column, endCell.Column) - NeighborCellMargin;
    int maxCol = Math.Max(startCell.Column, endCell.Column) + NeighborCellMargin;
    int minRow = Math.Min(startCell.Row, endCell.Row) - NeighborCellMargin;
    int maxRow = Math.Max(startCell.Row, endCell.Row) + NeighborCellMargin;
    return (minCol, maxCol, minRow, maxRow);
  }

  private static bool SegmentTouchesCell(
      GridSpec grid, GridCell cell, PositionXZ start, PositionXZ end)
  {
    double cellMinX = cell.Column * grid.CellSize;
    double cellMaxX = cellMinX + grid.CellSize;
    double cellMinZ = cell.Row * grid.CellSize;
    double cellMaxZ = cellMinZ + grid.CellSize;

    double minRatio = MinRatio;
    double maxRatio = MaxRatio;

    return ClipAxis(start.X, end.X, cellMinX, cellMaxX, ref minRatio, ref maxRatio)
        && ClipAxis(start.Z, end.Z, cellMinZ, cellMaxZ, ref minRatio, ref maxRatio);
  }

  private static bool ClipAxis(
      double segmentStart,
      double segmentEnd,
      double cellMin,
      double cellMax,
      ref double minRatio,
      ref double maxRatio)
  {
    double delta = segmentEnd - segmentStart;
    if (Math.Abs(delta) < Epsilon)
    {
      return segmentStart >= cellMin - Epsilon && segmentStart <= cellMax + Epsilon;
    }

    double entryRatio = (cellMin - segmentStart) / delta;
    double exitRatio = (cellMax - segmentStart) / delta;
    if (entryRatio > exitRatio)
    {
      (entryRatio, exitRatio) = (exitRatio, entryRatio);
    }

    minRatio = Math.Max(minRatio, entryRatio);
    maxRatio = Math.Min(maxRatio, exitRatio);
    return minRatio <= maxRatio + Epsilon;
  }
}

