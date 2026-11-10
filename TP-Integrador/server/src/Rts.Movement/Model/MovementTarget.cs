using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Movement.Model;

/// <summary>
/// Objetivo de una orden de tarea, reducido a su geometría: un punto (unidad, árbol) o una huella (edificio).
/// El rango hacia una huella se mide a su borde más cercano (D-019); hacia un punto, entre puntos.
/// </summary>
public sealed record MovementTarget
{
  private MovementTarget(EntityId id, PositionXZ? point, Footprint? footprint)
  {
    if (id.IsNone)
    {
      throw new ArgumentException("El objetivo debe tener un EntityId válido.", nameof(id));
    }

    Id = id;
    Point = point;
    Footprint = footprint;
  }

  public EntityId Id { get; }

  /// <summary>Posición del objetivo puntual; nulo si el objetivo es un edificio.</summary>
  public PositionXZ? Point { get; }

  /// <summary>Huella del edificio objetivo; nulo si el objetivo es puntual.</summary>
  public Footprint? Footprint { get; }

  public static MovementTarget AtPoint(EntityId id, PositionXZ point)
  {
    if (!point.IsFinite)
    {
      throw new ArgumentException("La posición debe ser finita.", nameof(point));
    }

    return new MovementTarget(id, point, null);
  }

  public static MovementTarget WithFootprint(EntityId id, Footprint footprint) => new(id, null, footprint);
}
