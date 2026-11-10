using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Economy.Contracts;

/// <summary>
/// Entrada inmutable de Economy para un tick (01 §6). Simulation la construye a partir del <c>TickSnapshot</c>
/// posterior a Movement y Construction; es la misma vista congelada que recibe Combat (06 §3).
/// <list type="bullet">
/// <item><see cref="Gatherers"/>: solo trabajadores habilitados (orden GatherWood activa y en rango; ver <see cref="GathererState"/>).</item>
/// <item><see cref="Trees"/>: todos los árboles del mapa, disponibles o agotados (los agotados se necesitan para regenerarlos).</item>
/// <item><see cref="Farms"/>: granjas completadas y existentes en el snapshot (las obras en construcción no se incluyen).</item>
/// </list>
/// El orden de las colecciones no afecta el resultado: Economy ordena internamente por ID.
/// El constructor verifica IDs únicos y que cada recolector apunte a un árbol presente.
/// </summary>
public sealed record EconomyTickInput
{
  public EconomyTickInput(
      TickId tick,
      EquatableArray<GathererState> gatherers,
      EquatableArray<TreeEconomyState> trees,
      EquatableArray<FarmEconomyState> farms)
  {
    EnsureNoNulls(gatherers, nameof(gatherers));
    EnsureNoNulls(trees, nameof(trees));
    EnsureNoNulls(farms, nameof(farms));
    EnsureUnique(gatherers.Select(g => g.WorkerId), nameof(gatherers));
    EnsureUnique(trees.Select(t => t.TreeId), nameof(trees));
    EnsureUnique(farms.Select(f => f.FarmId), nameof(farms));

    var treeIds = trees.Select(t => t.TreeId).ToHashSet();
    foreach (GathererState gatherer in gatherers)
    {
      if (!treeIds.Contains(gatherer.TreeId))
      {
        throw new ArgumentException(
            $"El recolector {gatherer.WorkerId} apunta al árbol {gatherer.TreeId}, que no está en la entrada.",
            nameof(gatherers));
      }
    }

    Tick = tick;
    Gatherers = gatherers;
    Trees = trees;
    Farms = farms;
  }

  public TickId Tick { get; }

  public EquatableArray<GathererState> Gatherers { get; }

  public EquatableArray<TreeEconomyState> Trees { get; }

  public EquatableArray<FarmEconomyState> Farms { get; }

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
