namespace Rts.Shared.Geometry;

/// <summary>
/// Especificación de la grilla del mapa y conversión entre posiciones continuas y celdas (D-004).
/// <list type="bullet">
/// <item>El mapa ocupa X ∈ [0, <see cref="WorldWidth"/>) y Z ∈ [0, <see cref="WorldDepth"/>) (intervalos semiabiertos).</item>
/// <item>Celda de una posición: <c>(floor(X / CellSize), floor(Z / CellSize))</c>.</item>
/// <item>Una unidad "ocupa" exactamente la celda que contiene su punto de posición (sin radio).</item>
/// </list>
/// Contiene solo la convención geométrica; las reglas de colocación, rutas y spawn pertenecen a sus módulos.
/// </summary>
public sealed record GridSpec
{
    public GridSpec(double cellSize, int columns, int rows)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "CellSize debe ser finito y positivo.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);

        CellSize = cellSize;
        Columns = columns;
        Rows = rows;
    }

    /// <summary>Lado de cada celda cuadrada, en unidades de mundo.</summary>
    public double CellSize { get; }

    /// <summary>Cantidad de columnas (eje X).</summary>
    public int Columns { get; }

    /// <summary>Cantidad de filas (eje Z).</summary>
    public int Rows { get; }

    public double WorldWidth => Columns * CellSize;

    public double WorldDepth => Rows * CellSize;

    public bool Contains(GridCell cell) =>
        cell.Column >= 0 && cell.Column < Columns &&
        cell.Row >= 0 && cell.Row < Rows;

    public bool Contains(PositionXZ position) =>
        position.IsFinite &&
        position.X >= 0 && position.X < WorldWidth &&
        position.Z >= 0 && position.Z < WorldDepth;

    public bool Contains(Footprint footprint) =>
        footprint.MinColumn >= 0 && footprint.EndColumn <= Columns &&
        footprint.MinRow >= 0 && footprint.EndRow <= Rows;

    /// <summary>
    /// Celda que contiene la posición. Para posiciones fuera del mapa devuelve una celda fuera de rango;
    /// el llamador debe validar con <see cref="Contains(PositionXZ)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Si la posición no es finita.</exception>
    public GridCell ToCell(PositionXZ position)
    {
        if (!position.IsFinite)
        {
            throw new ArgumentException("La posición debe ser finita.", nameof(position));
        }

        int column = checked((int)Math.Floor(position.X / CellSize));
        int row = checked((int)Math.Floor(position.Z / CellSize));

        // Una posición dentro del mapa siempre cae en una celda válida, aunque el redondeo de la división
        // flotante la empuje al borde exclusivo (p. ej. X apenas menor que WorldWidth).
        if (Contains(position))
        {
            column = Math.Min(column, Columns - 1);
            row = Math.Min(row, Rows - 1);
        }

        return new GridCell(column, row);
    }

    /// <summary>Centro geométrico de la celda en coordenadas de mundo.</summary>
    public PositionXZ CellCenter(GridCell cell) =>
        new((cell.Column + 0.5) * CellSize, (cell.Row + 0.5) * CellSize);

    /// <summary>Extensión de la huella en coordenadas de mundo (del borde inicial de su primera celda al borde final de la última).</summary>
    public WorldRect ToWorldRect(Footprint footprint) =>
        new(
            footprint.MinColumn * CellSize,
            footprint.MinRow * CellSize,
            footprint.EndColumn * CellSize,
            footprint.EndRow * CellSize);
}
