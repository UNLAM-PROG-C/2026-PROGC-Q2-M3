using System.Collections.Frozen;
using Rts.Shared.Geometry;

namespace Rts.Construction.Model;

/// <summary>
/// Vista inmutable de ocupación para validar colocaciones (03 §2). Simulation la construye desde el mapa oficial
/// y la **actualiza entre comandos** del mismo lote, para que dos colocaciones sobre la misma celda en un tick
/// se resuelvan por Sequence (la segunda ve la primera).
/// <list type="bullet">
/// <item><see cref="StructureCells"/>: huellas de edificios y vallas, completados o en obra.</item>
/// <item><see cref="TreeReservedCells"/>: celdas reservadas de árboles, visibles o agotados.</item>
/// <item><see cref="UnitCells"/>: celdas que contienen el punto de alguna unidad de cualquier jugador (D-004).</item>
/// </list>
/// </summary>
public sealed class OccupancyView
{
  private readonly FrozenSet<GridCell> _structures;
  private readonly FrozenSet<GridCell> _trees;
  private readonly FrozenSet<GridCell> _units;

  public OccupancyView(
      GridSpec grid,
      IEnumerable<GridCell> structureCells,
      IEnumerable<GridCell> treeReservedCells,
      IEnumerable<GridCell> unitCells)
  {
    ArgumentNullException.ThrowIfNull(grid);
    Grid = grid;
    _structures = ToInsideSet(grid, structureCells, nameof(structureCells));
    _trees = ToInsideSet(grid, treeReservedCells, nameof(treeReservedCells));
    _units = ToInsideSet(grid, unitCells, nameof(unitCells));
  }

  public GridSpec Grid { get; }

  public IReadOnlySet<GridCell> StructureCells => _structures;

  public IReadOnlySet<GridCell> TreeReservedCells => _trees;

  public IReadOnlySet<GridCell> UnitCells => _units;

  /// <summary>Copia con una huella agregada como estructura (tras confirmar una colocación dentro del mismo lote).</summary>
  public OccupancyView WithStructure(Footprint footprint)
  {
    if (!Grid.Contains(footprint))
    {
      throw new ArgumentException("La huella debe estar dentro del mapa.", nameof(footprint));
    }

    return new OccupancyView(Grid, _structures.Concat(footprint.Cells()), _trees, _units);
  }

  private static FrozenSet<GridCell> ToInsideSet(GridSpec grid, IEnumerable<GridCell> cells, string paramName)
  {
    ArgumentNullException.ThrowIfNull(cells, paramName);
    FrozenSet<GridCell> set = cells.ToFrozenSet();
    foreach (GridCell cell in set)
    {
      if (!grid.Contains(cell))
      {
        throw new ArgumentException($"Celda fuera del mapa: {cell}.", paramName);
      }
    }

    return set;
  }
}
