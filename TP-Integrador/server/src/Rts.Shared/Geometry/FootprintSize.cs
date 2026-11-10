namespace Rts.Shared.Geometry;

/// <summary>
/// Tamaño de una huella rectangular en celdas (D-006). <see cref="Width"/> se mide en columnas (eje X)
/// y <see cref="Height"/> en filas (eje Z). Sin rotación. Ambas dimensiones son positivas.
/// </summary>
public readonly record struct FootprintSize
{
    public FootprintSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public int CellCount => Width * Height;

    public override string ToString() => $"{Width}x{Height}";
}
