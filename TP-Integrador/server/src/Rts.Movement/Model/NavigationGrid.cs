using System.Collections.Frozen;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement.Model;

/// <summary>
/// Vista inmutable de navegación del servidor (04 §4): grilla y celdas bloqueadas por edificios y vallas, en obra o
/// completados (04 §3). Árboles y unidades **no** bloquean. Las celdas fuera del mapa cuentan como bloqueadas.
/// Simulation la construye desde el mapa oficial; Construction puede pedir una copia con una huella hipotética
/// (<see cref="WithBlockedFootprint"/>) para validar alcanzabilidad antes de aceptar un Build (03 §5).
/// </summary>
public sealed class NavigationGrid
{
    private readonly FrozenSet<GridCell> _blocked;

    public NavigationGrid(GridSpec grid, MapRevision revision, IEnumerable<GridCell> blockedCells)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(blockedCells);

        FrozenSet<GridCell> blocked = blockedCells.ToFrozenSet();
        foreach (GridCell cell in blocked)
        {
            if (!grid.Contains(cell))
            {
                throw new ArgumentException($"Celda bloqueada fuera del mapa: {cell}.", nameof(blockedCells));
            }
        }

        Grid = grid;
        Revision = revision;
        _blocked = blocked;
    }

    public GridSpec Grid { get; }

    public MapRevision Revision { get; }

    public IReadOnlySet<GridCell> BlockedCells => _blocked;

    public bool IsBlocked(GridCell cell) => !Grid.Contains(cell) || _blocked.Contains(cell);

    /// <summary>Copia con la huella agregada como bloqueada (colocación hipotética); conserva la revisión.</summary>
    /// <exception cref="ArgumentException">Si la huella no está completamente dentro del mapa.</exception>
    public NavigationGrid WithBlockedFootprint(Footprint footprint)
    {
        if (!Grid.Contains(footprint))
        {
            throw new ArgumentException("La huella hipotética debe estar dentro del mapa.", nameof(footprint));
        }

        return new NavigationGrid(Grid, Revision, _blocked.Concat(footprint.Cells()));
    }
}
