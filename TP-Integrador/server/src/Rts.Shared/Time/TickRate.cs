namespace Rts.Shared.Time;

/// <summary>
/// Frecuencia de Simulation. DECIDIDO (06 §3): 20 Hz objetivo (50 ms por tick).
/// RECOMENDADO (O-007): derivar conversiones de este valor (p. ej. velocidad por tick = SpeedPerSecond / TicksPerSecond)
/// en lugar de usar la constante 20 en las fórmulas.
/// </summary>
public sealed record TickRate
{
  /// <summary>Valor aprobado: 20 ticks por segundo.</summary>
  public static readonly TickRate Default = new(20);

  public TickRate(int ticksPerSecond)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(ticksPerSecond, 1000);
    TicksPerSecond = ticksPerSecond;
  }

  public int TicksPerSecond { get; }

  /// <summary>Intervalo objetivo entre ticks (50 ms a 20 Hz).</summary>
  public TimeSpan TickInterval => TimeSpan.FromTicks(TimeSpan.TicksPerSecond / TicksPerSecond);

  /// <summary>Convierte una magnitud expresada por segundo en su equivalente por tick.</summary>
  public double PerTick(double perSecond) => perSecond / TicksPerSecond;
}
