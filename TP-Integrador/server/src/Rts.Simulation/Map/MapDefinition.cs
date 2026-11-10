using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Simulation.Map;

/// <summary>Unidad que existe al comenzar la partida.</summary>
public sealed record InitialUnitDefinition(UnitType UnitType, PositionXZ Position);

/// <summary>Punto de partida de un jugador: celda ancla de su Centro Principal y sus unidades iniciales (06 §2).</summary>
public sealed record PlayerStartDefinition(PlayerId Player, GridCell MainCenterAnchor, EquatableArray<InitialUnitDefinition> InitialUnits);

/// <summary>
/// Mapa fijo de la partida. DECIDIDO (D-046): se carga desde un archivo del servidor, que es la única fuente de verdad;
/// Godot reconstruye el layout jugable a partir del InitialState. El mapa debe ser simétrico (06 §2): esa simetría es
/// responsabilidad del diseño del archivo y no se verifica automáticamente.
/// Los recursos iniciales salen de <c>EconomyConfig.InitialBalance</c> y las huellas, de <c>ConstructionConfig</c>.
/// </summary>
public sealed record MapDefinition(
    GridSpec Grid,
    EquatableArray<GridCell> TreeCells,
    EquatableArray<PlayerStartDefinition> Starts)
{
  /// <summary>
  /// Problemas del mapa, considerando la huella del Centro Principal (<paramref name="mainCenterSize"/>):
  /// límites, árboles duplicados o fuera del mapa, exactamente un inicio por jugador, Centros Principales sin
  /// superponerse entre sí ni con árboles, y unidades iniciales dentro del mapa y fuera de toda huella.
  /// </summary>
  public IReadOnlyList<string> GetValidationErrors(FootprintSize mainCenterSize)
  {
    var errors = new List<string>();

    if (Grid is null)
    {
      return ["Grid es obligatorio."];
    }

    var treeSet = new HashSet<GridCell>();
    foreach (GridCell tree in TreeCells)
    {
      if (!Grid.Contains(tree))
      {
        errors.Add($"Árbol fuera del mapa: {tree}.");
      }

      if (!treeSet.Add(tree))
      {
        errors.Add($"Árbol duplicado: {tree}.");
      }
    }

    if (Starts.Count != 2 || !Starts.Any(s => s?.Player == PlayerId.One) || !Starts.Any(s => s?.Player == PlayerId.Two))
    {
      errors.Add("El mapa debe tener exactamente un inicio para P1 y otro para P2.");
      return errors;
    }

    var footprints = new List<Footprint>();
    foreach (PlayerStartDefinition start in Starts)
    {
      var footprint = new Footprint(start.MainCenterAnchor, mainCenterSize);
      if (!Grid.Contains(footprint))
      {
        errors.Add($"El Centro Principal de {start.Player} sale del mapa.");
      }

      if (footprint.Cells().Any(treeSet.Contains))
      {
        errors.Add($"El Centro Principal de {start.Player} se superpone con un árbol.");
      }

      footprints.Add(footprint);
    }

    if (footprints[0].Intersects(footprints[1]))
    {
      errors.Add("Los Centros Principales se superponen.");
    }

    foreach (PlayerStartDefinition start in Starts)
    {
      foreach (InitialUnitDefinition unit in start.InitialUnits)
      {
        if (!Enum.IsDefined(unit.UnitType) || !Grid.Contains(unit.Position))
        {
          errors.Add($"Unidad inicial inválida o fuera del mapa para {start.Player}: {unit.UnitType} en {unit.Position}.");
        }
        else if (footprints.Any(f => f.Contains(Grid.ToCell(unit.Position))))
        {
          errors.Add($"Unidad inicial de {start.Player} dentro de un Centro Principal: {unit.Position}.");
        }
      }
    }

    return errors;
  }
}
