namespace Rts.Shared.Geometry;

/// <summary>
/// Posición oficial continua sobre el plano lógico X/Z, en unidades de mundo (D-004).
/// La altura (Y) de Godot no es autoridad de movimiento. Solo el servidor publica posiciones oficiales.
/// </summary>
public readonly record struct PositionXZ(double X, double Z)
{
  public static readonly PositionXZ Origin = default;

  /// <summary>Falso si alguna coordenada es NaN o infinita (entrada inválida que debe rechazarse).</summary>
  public bool IsFinite => double.IsFinite(X) && double.IsFinite(Z);

  public double DistanceSquaredTo(PositionXZ other)
  {
    double dx = other.X - X;
    double dz = other.Z - Z;
    return (dx * dx) + (dz * dz);
  }

  public double DistanceTo(PositionXZ other) => Math.Sqrt(DistanceSquaredTo(other));

  /// <summary>Verdadero si la distancia a <paramref name="other"/> es menor o igual a <paramref name="range"/> (comparación inclusiva, D-019).</summary>
  public bool IsWithinRange(PositionXZ other, double range) => DistanceSquaredTo(other) <= range * range;

  public override string ToString() => $"({X}, {Z})";
}
