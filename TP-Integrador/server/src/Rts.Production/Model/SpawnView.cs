using System.Collections.Frozen;
using Rts.Shared.Geometry;

namespace Rts.Production.Model;

/// <summary>
/// Vista inmutable para buscar celdas de aparición (05 §5), con el mapa ya confirmado tras Combat.
/// Una celda es apta si está dentro del mapa y no está en <see cref="StructureCells"/> (edificios y vallas),
/// <see cref="TreeReservedCells"/> (D-040) ni <see cref="UnitCells"/> (celdas con el punto de una unidad, D-004).
/// Las celdas tomadas por otros spawns del mismo tick las reserva el propio cálculo.
/// </summary>
public sealed class SpawnView
{
    private readonly FrozenSet<GridCell> _structures;
    private readonly FrozenSet<GridCell> _trees;
    private readonly FrozenSet<GridCell> _units;

    public SpawnView(
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
