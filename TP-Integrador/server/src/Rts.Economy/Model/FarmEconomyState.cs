using Rts.Shared.Ids;

namespace Rts.Economy.Model;

/// <summary>
/// Vista económica inmutable de una granja completada y existente. Entrada (snapshot) y estado propuesto en el resultado.
/// HP, huella y progreso de reparación no forman parte de esta vista (D-012).
/// <para>
/// <see cref="ProductionActiveFromTick"/>: primer tick en que el temporizador avanza. Simulation lo fija en N+1
/// cuando la granja se completa en el tick N (01 §4) y también cuando su reparación se completa en el tick N (D-014).
/// </para>
/// Invariantes: <see cref="FarmProductionStatus.Producing"/> ⇒ tandas &gt; 0;
/// <see cref="FarmProductionStatus.Depleted"/> ⇒ tandas = 0 y temporizador en 0.
/// </summary>
public sealed record FarmEconomyState
{
  public FarmEconomyState(
      EntityId farmId,
      PlayerId owner,
      FarmProductionStatus status,
      int batchesRemaining,
      int ticksAccumulated,
      TickId productionActiveFromTick)
  {
    if (farmId.IsNone)
    {
      throw new ArgumentException("La granja debe tener un EntityId válido.", nameof(farmId));
    }

    if (!owner.IsValid)
    {
      throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
    }

    ArgumentOutOfRangeException.ThrowIfNegative(batchesRemaining);
    ArgumentOutOfRangeException.ThrowIfNegative(ticksAccumulated);

    bool valid = status switch
    {
      FarmProductionStatus.Producing => batchesRemaining > 0,
      FarmProductionStatus.Depleted => batchesRemaining == 0 && ticksAccumulated == 0,
      _ => false,
    };

    if (!valid)
    {
      throw new ArgumentException(
          $"Estado de granja incoherente: Status={status}, BatchesRemaining={batchesRemaining}, TicksAccumulated={ticksAccumulated}.");
    }

    FarmId = farmId;
    Owner = owner;
    Status = status;
    BatchesRemaining = batchesRemaining;
    TicksAccumulated = ticksAccumulated;
    ProductionActiveFromTick = productionActiveFromTick;
  }

  public EntityId FarmId { get; }

  public PlayerId Owner { get; }

  public FarmProductionStatus Status { get; }

  /// <summary>Tandas que todavía puede producir en el ciclo actual (5 al completarse o repararse).</summary>
  public int BatchesRemaining { get; }

  /// <summary>Ticks productivos acumulados desde la última tanda o desde el reinicio del temporizador.</summary>
  public int TicksAccumulated { get; }

  public TickId ProductionActiveFromTick { get; }
}
