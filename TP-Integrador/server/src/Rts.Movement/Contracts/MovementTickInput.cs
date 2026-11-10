using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Movement.Contracts;

/// <summary>
/// Entrada inmutable de la fase 2 (Movement), después de procesar el lote de comandos (06 §3, 04 §7).
/// <list type="bullet">
/// <item><see cref="Map"/>: navegación vigente, con la <c>MapRevision</c> actual (incluye las colocaciones aceptadas en la fase 1).</item>
/// <item><see cref="Units"/>: todas las unidades vivas con su orden y su ruta.</item>
/// <item><see cref="Targets"/>: geometría de las entidades apuntadas por órdenes de tarea. Si falta el objetivo de una
/// unidad, Movement termina su orden con <see cref="OrderEndReason.TargetLost"/> (caso defensivo).</item>
/// </list>
/// El orden de las colecciones no afecta el resultado. El constructor verifica IDs únicos.
/// </summary>
public sealed record MovementTickInput
{
  public MovementTickInput(
      TickId tick,
      NavigationGrid map,
      EquatableArray<UnitMovementState> units,
      EquatableArray<MovementTarget> targets)
  {
    ArgumentNullException.ThrowIfNull(map);
    EnsureUnique(units.Select(u => u?.UnitId ?? throw new ArgumentNullException(nameof(units))), nameof(units));
    EnsureUnique(targets.Select(t => t?.Id ?? throw new ArgumentNullException(nameof(targets))), nameof(targets));

    Tick = tick;
    Map = map;
    Units = units;
    Targets = targets;
  }

  public TickId Tick { get; }

  public NavigationGrid Map { get; }

  public EquatableArray<UnitMovementState> Units { get; }

  public EquatableArray<MovementTarget> Targets { get; }

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
