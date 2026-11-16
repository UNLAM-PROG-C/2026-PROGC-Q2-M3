using Rts.Production.Model;
using Rts.Shared.Geometry;

namespace Rts.Production.Calculation;

/// <summary>
/// Búsqueda determinista de la celda donde aparece una unidad nueva (contrato 05 §4, D-039 y D-040).
/// <list type="bullet">
/// <item>Se recorren anillos de celdas alrededor de la huella, de 1 a <c>SpawnSearchRadiusCells</c>: el anillo <c>r</c> son las
/// celdas a distancia de Chebyshev exactamente <c>r</c> del rectángulo de la huella.</item>
/// <item>Dentro de un anillo gana la celda cuyo centro está más cerca del centro de la huella; los empates se resuelven por
/// fila y luego por columna, ascendentes.</item>
/// <item>Una celda es apta si está dentro del mapa, sin estructura, sin reserva de árbol, sin el punto de ninguna unidad y no
/// elegida ya por otra aparición del mismo tick.</item>
/// </list>
/// </summary>
internal static class SpawnCellFinder
{
  private const int FirstRing = 1;

  // El fin de una huella es exclusivo (Anchor + Size): la última fila o columna ocupada es una antes, y un rango inclusivo de
  // índices tiene uno más que su diferencia.
  private const int InclusiveEnd = 1;

  // La distancia se compara en el DOBLE de las unidades de celda: el centro de una celda y el de una huella caen en múltiplos
  // de medio lado, así que duplicar todo deja enteros. Con enteros dos celdas simétricas empatan exactamente, sin el ruido de
  // punto flotante que alteraría el orden fijado por el contrato. Como las celdas son cuadradas, el orden coincide con el de
  // las distancias en coordenadas de mundo.
  private const int Doubled = 2;
  private const int HalfCell = 1;

  /// <summary>Primera celda apta, o <c>null</c> si no hay ninguna dentro del radio.</summary>
  public static GridCell? Find(Footprint footprint, SpawnView view, IReadOnlySet<GridCell> reserved, int radiusCells)
  {
    for (int ring = FirstRing; ring <= radiusCells; ring++)
    {
      GridCell? best = BestInRing(footprint, ring, view, reserved);
      if (best is not null)
      {
        return best;
      }
    }

    return null;
  }

  private static GridCell? BestInRing(Footprint footprint, int ring, SpawnView view, IReadOnlySet<GridCell> reserved) =>
      RingCells(footprint, ring)
          .Where(cell => IsSuitable(cell, view, reserved))
          .OrderBy(cell => DoubledDistanceSquared(cell, footprint))
          .ThenBy(cell => cell.Row)
          .ThenBy(cell => cell.Column)
          .Select(cell => (GridCell?)cell)
          .FirstOrDefault();

  /// <summary>Las celdas del borde del rectángulo de la huella agrandado en <paramref name="ring"/> celdas por lado.</summary>
  private static IEnumerable<GridCell> RingCells(Footprint footprint, int ring)
  {
    int firstColumn = footprint.MinColumn - ring;
    int lastColumn = footprint.EndColumn - InclusiveEnd + ring;
    int firstRow = footprint.MinRow - ring;
    int lastRow = footprint.EndRow - InclusiveEnd + ring;

    return Enumerable.Range(firstRow, lastRow - firstRow + InclusiveEnd)
        .SelectMany(row => Enumerable.Range(firstColumn, lastColumn - firstColumn + InclusiveEnd).Select(column => new GridCell(column, row)))
        .Where(cell => cell.Column == firstColumn || cell.Column == lastColumn || cell.Row == firstRow || cell.Row == lastRow);
  }

  private static bool IsSuitable(GridCell cell, SpawnView view, IReadOnlySet<GridCell> reserved) =>
      view.Grid.Contains(cell)
      && !view.StructureCells.Contains(cell)
      && !view.TreeReservedCells.Contains(cell)
      && !view.UnitCells.Contains(cell)
      && !reserved.Contains(cell);

  /// <summary>Distancia al cuadrado entre el centro de la celda y el de la huella, medida en el doble de las unidades de celda.</summary>
  private static int DoubledDistanceSquared(GridCell cell, Footprint footprint)
  {
    int deltaColumn = (Doubled * cell.Column) + HalfCell - (footprint.MinColumn + footprint.EndColumn);
    int deltaRow = (Doubled * cell.Row) + HalfCell - (footprint.MinRow + footprint.EndRow);
    return (deltaColumn * deltaColumn) + (deltaRow * deltaRow);
  }
}
