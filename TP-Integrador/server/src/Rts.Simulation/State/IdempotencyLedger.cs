using System.Collections.Immutable;
using Rts.Shared.Commands;
using Rts.Shared.Ids;

namespace Rts.Simulation.State;

/// <summary>
/// Registro inmutable de resultados por clave de idempotencia (D-043). Solo lo usa el hilo de Simulation, sin locks.
/// Se conserva durante toda la partida. Si un comando llega con una clave ya registrada, no se ejecuta y se reenvía el
/// resultado original. Como la cola es FIFO, el original siempre se procesa antes que sus duplicados.
/// </summary>
public sealed class IdempotencyLedger
{
  public static readonly IdempotencyLedger Empty = new(ImmutableDictionary<IdempotencyKey, CommandResult>.Empty);

  private readonly ImmutableDictionary<IdempotencyKey, CommandResult> _results;

  private IdempotencyLedger(ImmutableDictionary<IdempotencyKey, CommandResult> results) => _results = results;

  public int Count => _results.Count;

  public bool TryGet(IdempotencyKey key, out CommandResult? result) => _results.TryGetValue(key, out result);

  /// <summary>Devuelve un registro nuevo con el resultado agregado.</summary>
  /// <exception cref="InvalidOperationException">Si la clave ya estaba registrada: un comando no se procesa dos veces.</exception>
  public IdempotencyLedger Record(CommandResult result)
  {
    ArgumentNullException.ThrowIfNull(result);
    if (_results.ContainsKey(result.Key))
    {
      throw new InvalidOperationException($"La clave {result.Key} ya tiene resultado; un comando no se procesa dos veces.");
    }

    return new IdempotencyLedger(_results.Add(result.Key, result));
  }
}
