namespace Rts.Shared.Geometry;

/// <summary>
/// Celda de la grilla de ocupación (D-004). <see cref="Column"/> crece con X y <see cref="Row"/> crece con Z;
/// la celda (0,0) está en la esquina del mapa donde X = 0 y Z = 0.
/// </summary>
public readonly record struct GridCell(int Column, int Row)
{
  public override string ToString() => $"[{Column},{Row}]";
}
