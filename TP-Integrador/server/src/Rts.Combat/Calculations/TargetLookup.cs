using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Combat.Calculations;

/// <summary>Dueño del objetivo y su distancia al cuadrado al atacante.</summary>
internal readonly record struct ResolvedTarget(PlayerId Owner, double DistanceSquared);

/// <summary>
/// Búsqueda de objetivos en <c>Units ∪ Buildings</c> del snapshot (contrato 02 §4, regla 1). Se construye una vez por
/// llamada y solo se lee, así que no se comparte estado mutable entre subcálculos.
/// </summary>
internal sealed class TargetLookup
{
  private readonly Dictionary<EntityId, CombatUnit> _units;
  private readonly Dictionary<EntityId, CombatBuilding> _buildings;
  private readonly GridSpec _grid;

  public TargetLookup(CombatTickInput input)
  {
    _units = input.Units.ToDictionary(u => u.Id);
    _buildings = input.Buildings.ToDictionary(b => b.Id);
    _grid = input.Grid;
  }

  /// <summary>Unidad que la entrada garantiza presente (el constructor de la entrada verifica a los atacantes).</summary>
  public CombatUnit GetUnit(EntityId id) => _units[id];

  /// <summary>
  /// Distancias (DECIDIDO D-019, D-020): contra una unidad, entre puntos; contra un edificio, al borde más cercano de su
  /// huella. Devuelve <c>null</c> si el objetivo no existe en el snapshot.
  /// </summary>
  public ResolvedTarget? ResolveTarget(EntityId targetId, PositionXZ from)
  {
    if (_units.TryGetValue(targetId, out CombatUnit? unit))
    {
      return new ResolvedTarget(unit.Owner, unit.Position.DistanceSquaredTo(from));
    }

    if (_buildings.TryGetValue(targetId, out CombatBuilding? building))
    {
      return new ResolvedTarget(building.Owner, _grid.ToWorldRect(building.Footprint).DistanceSquaredTo(from));
    }

    return null;
  }
}
