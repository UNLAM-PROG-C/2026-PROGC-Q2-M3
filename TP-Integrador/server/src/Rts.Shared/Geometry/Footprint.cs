namespace Rts.Shared.Geometry;

/// <summary>
/// Huella rectangular ubicada en la grilla (D-006). <see cref="Anchor"/> es la celda de columna y fila mínimas;
/// la huella cubre las columnas [Anchor.Column, Anchor.Column + Width) y las filas [Anchor.Row, Anchor.Row + Height).
/// </summary>
public readonly record struct Footprint(GridCell Anchor, FootprintSize Size)
{
    public int MinColumn => Anchor.Column;

    public int MinRow => Anchor.Row;

    /// <summary>Columna inmediatamente posterior a la última cubierta (exclusiva).</summary>
    public int EndColumn => Anchor.Column + Size.Width;

    /// <summary>Fila inmediatamente posterior a la última cubierta (exclusiva).</summary>
    public int EndRow => Anchor.Row + Size.Height;

    public bool Contains(GridCell cell) =>
        cell.Column >= MinColumn && cell.Column < EndColumn &&
        cell.Row >= MinRow && cell.Row < EndRow;

    public bool Intersects(Footprint other) =>
        MinColumn < other.EndColumn && other.MinColumn < EndColumn &&
        MinRow < other.EndRow && other.MinRow < EndRow;

    /// <summary>
    /// Celdas cubiertas en orden determinista: fila ascendente y, dentro de cada fila, columna ascendente.
    /// </summary>
    public IEnumerable<GridCell> Cells()
    {
        for (int row = MinRow; row < EndRow; row++)
        {
            for (int column = MinColumn; column < EndColumn; column++)
            {
                yield return new GridCell(column, row);
            }
        }
    }

    public override string ToString() => $"{Anchor}+{Size}";
}
