using Rts.Combat.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Combat.Contracts;

/// <summary>
/// Entrada inmutable de Combat para un tick (02 §6). Simulation la construye desde el mismo <c>TickSnapshot</c>
/// que recibe Economy, posterior a Movement y Construction (06 §3).
/// <list type="bullet">
/// <item><see cref="Units"/> y <see cref="Buildings"/>: todas las entidades vivas, como posibles objetivos.</item>
/// <item><see cref="Soldiers"/>: solo los soldados con orden Attack activa.</item>
/// <item><see cref="Towers"/>: todas las torres completadas; si ya son operativas lo decide Combat con <see cref="Tick"/>.</item>
/// <item><see cref="Grid"/>: convención de grilla para convertir huellas a coordenadas de mundo.</item>
/// </list>
/// El orden de las colecciones no afecta el resultado. El constructor verifica IDs únicos en el espacio global
/// (unidades y edificios juntos) y que cada atacante referencie una entidad del tipo correcto. El objetivo de un
/// soldado puede faltar: es un caso defensivo y ese soldado se propone a Idle.
/// </summary>
public sealed record CombatTickInput
{
  public CombatTickInput(
      TickId tick,
      GridSpec grid,
      EquatableArray<CombatUnit> units,
      EquatableArray<CombatBuilding> buildings,
      EquatableArray<SoldierAttacker> soldiers,
      EquatableArray<TowerAttacker> towers)
  {
    ArgumentNullException.ThrowIfNull(grid);
    EnsureNoNulls(units, nameof(units));
    EnsureNoNulls(buildings, nameof(buildings));
    EnsureNoNulls(soldiers, nameof(soldiers));
    EnsureNoNulls(towers, nameof(towers));

    EnsureUnique(units.Select(u => u.Id).Concat(buildings.Select(b => b.Id)), "units/buildings");
    EnsureUnique(soldiers.Select(s => s.SoldierId), nameof(soldiers));
    EnsureUnique(towers.Select(t => t.TowerId), nameof(towers));

    var unitsById = units.ToDictionary(u => u.Id);
    foreach (SoldierAttacker soldier in soldiers)
    {
      if (!unitsById.TryGetValue(soldier.SoldierId, out CombatUnit? unit) || unit.Type != UnitType.Soldier)
      {
        throw new ArgumentException($"El atacante {soldier.SoldierId} no es un soldado presente en la entrada.", nameof(soldiers));
      }
    }

    var buildingsById = buildings.ToDictionary(b => b.Id);
    foreach (TowerAttacker tower in towers)
    {
      if (!buildingsById.TryGetValue(tower.TowerId, out CombatBuilding? building) ||
          building.Type != BuildingType.Tower ||
          !building.IsCompleted)
      {
        throw new ArgumentException($"El atacante {tower.TowerId} no es una torre completada presente en la entrada.", nameof(towers));
      }
    }

    Tick = tick;
    Grid = grid;
    Units = units;
    Buildings = buildings;
    Soldiers = soldiers;
    Towers = towers;
  }

  public TickId Tick { get; }

  public GridSpec Grid { get; }

  public EquatableArray<CombatUnit> Units { get; }

  public EquatableArray<CombatBuilding> Buildings { get; }

  public EquatableArray<SoldierAttacker> Soldiers { get; }

  public EquatableArray<TowerAttacker> Towers { get; }

  private static void EnsureNoNulls<T>(EquatableArray<T> items, string paramName)
      where T : class
  {
    foreach (T item in items)
    {
      ArgumentNullException.ThrowIfNull(item, paramName);
    }
  }

  private static void EnsureUnique(IEnumerable<EntityId> ids, string paramName)
  {
    var seen = new HashSet<EntityId>();
    foreach (EntityId id in ids)
    {
      if (!seen.Add(id))
      {
        throw new ArgumentException($"ID duplicado en la entrada: {id}.", paramName);
      }
    }
  }
}
