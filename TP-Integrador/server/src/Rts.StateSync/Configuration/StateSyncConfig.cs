using Rts.Shared.Time;

namespace Rts.StateSync.Configuration;

/// <summary>
/// Parámetros de State Sync (09 §11). Se valida una vez al iniciar el servidor, junto con el <see cref="TickRate"/> de Simulation.
/// </summary>
public sealed record StateSyncConfig
{
  /// <summary>Valor acordado de 10 Hz (DECIDIDO 09 §5).</summary>
  public const int DefaultPositionRateHz = 10;

  /// <summary>
  /// Frecuencia de publicación de posiciones. DECIDIDO (D-062): se publica en los ticks múltiplos de
  /// <c>TickRate / PositionRateHz</c>, que debe ser entero (2 a 20 Hz).
  /// </summary>
  public int PositionRateHz { get; init; } = DefaultPositionRateHz;

  /// <summary>
  /// Decimales con que se redondean las coordenadas publicadas (RECOMENDADO D-070: 3, milímetros). Solo afecta la vista: el
  /// servidor conserva las posiciones exactas.
  /// </summary>
  public int PositionDecimals { get; init; } = 3;

  /// <summary>Ticks entre publicaciones de posiciones (2 con los valores aprobados).</summary>
  /// <exception cref="ArgumentException">Si la configuración no es válida para ese tick rate.</exception>
  public int GetPositionIntervalTicks(TickRate tickRate)
  {
    Validate(tickRate);
    return tickRate.TicksPerSecond / PositionRateHz;
  }

  /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
  public IReadOnlyList<string> GetValidationErrors(TickRate tickRate)
  {
    ArgumentNullException.ThrowIfNull(tickRate);
    var errors = new List<string>();

    if (PositionRateHz <= 0 || PositionRateHz > tickRate.TicksPerSecond)
    {
      errors.Add($"{nameof(PositionRateHz)} debe estar entre 1 y el tick rate ({PositionRateHz}, tick rate {tickRate.TicksPerSecond}).");
    }
    else if (tickRate.TicksPerSecond % PositionRateHz != 0)
    {
      errors.Add($"El tick rate ({tickRate.TicksPerSecond}) debe ser múltiplo de {nameof(PositionRateHz)} ({PositionRateHz}) para publicar cada N ticks enteros.");
    }

    if (PositionDecimals is < 0 or > 6)
    {
      errors.Add($"{nameof(PositionDecimals)} debe estar entre 0 y 6 ({PositionDecimals}).");
    }

    return errors;
  }

  /// <summary>Valida la configuración completa.</summary>
  /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
  public void Validate(TickRate tickRate)
  {
    IReadOnlyList<string> errors = GetValidationErrors(tickRate);
    if (errors.Count > 0)
    {
      throw new ArgumentException("Configuración de State Sync inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
  }
}
