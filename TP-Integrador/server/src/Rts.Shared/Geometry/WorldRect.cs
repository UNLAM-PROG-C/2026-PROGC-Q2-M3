namespace Rts.Shared.Geometry;

/// <summary>
/// Rectángulo alineado a los ejes en coordenadas de mundo X/Z; normalmente la extensión de una huella de edificio
/// (ver <see cref="GridSpec.ToWorldRect"/>). DECIDIDO (D-019, D-020): toda distancia que involucra un edificio se mide
/// al **borde más cercano** de su huella; una posición dentro del rectángulo está a distancia 0.
/// </summary>
public readonly record struct WorldRect
{
  public WorldRect(double minX, double minZ, double maxX, double maxZ)
  {
    if (!double.IsFinite(minX) || !double.IsFinite(minZ) || !double.IsFinite(maxX) || !double.IsFinite(maxZ))
    {
      throw new ArgumentException("Las coordenadas del rectángulo deben ser finitas.");
    }

    if (minX > maxX || minZ > maxZ)
    {
      throw new ArgumentException($"Rectángulo invertido: X [{minX}, {maxX}], Z [{minZ}, {maxZ}].");
    }

    MinX = minX;
    MinZ = minZ;
    MaxX = maxX;
    MaxZ = maxZ;
  }

  public double MinX { get; }

  public double MinZ { get; }

  public double MaxX { get; }

  public double MaxZ { get; }

  public PositionXZ Center => new((MinX + MaxX) / 2, (MinZ + MaxZ) / 2);

  /// <summary>Punto del rectángulo más cercano a <paramref name="position"/> (la propia posición si está dentro).</summary>
  public PositionXZ ClosestPointTo(PositionXZ position) =>
      new(Math.Clamp(position.X, MinX, MaxX), Math.Clamp(position.Z, MinZ, MaxZ));

  public double DistanceSquaredTo(PositionXZ position) => ClosestPointTo(position).DistanceSquaredTo(position);

  public double DistanceTo(PositionXZ position) => Math.Sqrt(DistanceSquaredTo(position));

  /// <summary>Verdadero si la distancia al borde más cercano es menor o igual a <paramref name="range"/> (comparación inclusiva).</summary>
  public bool IsWithinRange(PositionXZ position, double range) => DistanceSquaredTo(position) <= range * range;

  public override string ToString() => $"X[{MinX}, {MaxX}] Z[{MinZ}, {MaxZ}]";
}
