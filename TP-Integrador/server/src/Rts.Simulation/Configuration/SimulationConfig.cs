using Rts.Shared.Time;

namespace Rts.Simulation.Configuration;

/// <summary>Nivel de diagnóstico (06 §10): resumen y errores en ejecución normal; detalle por tick en debug.</summary>
public enum DiagnosticsLevel
{
  Summary = 1,
  Debug = 2,
}

/// <summary>
/// Parámetros de Simulation (06 §8, §10). Valores PENDIENTES salvo <see cref="TickRate"/> = 20 Hz (DECIDIDO).
/// Se valida una vez al iniciar el servidor.
/// </summary>
public sealed record SimulationConfig
{
  public TickRate TickRate { get; init; } = TickRate.Default;

  /// <summary>Máximo de comandos que se toman de la cola por tick; el resto ya admitido espera (06 §4).</summary>
  public required int MaxCommandsPerTick { get; init; }

  /// <summary>Capacidad finita de la cola global de comandos.</summary>
  public required int CommandQueueCapacity { get; init; }

  /// <summary>
  /// Plazo de seguridad de la barrera Economy ∥ Combat. Si se excede, la tarea se considera bloqueada y es una falla crítica
  /// (06 §7). Debe ser holgado respecto de 50 ms: un tick lento no es una falla.
  /// </summary>
  public required TimeSpan BarrierTimeout { get; init; }

  /// <summary>
  /// Plazo de seguridad de un tick completo para el supervisor del loop (06 §6): si un tick sigue en curso pasado este plazo, se
  /// considera bloqueado y es una falla crítica. Debe superar a <see cref="BarrierTimeout"/>, para que una barrera vencida se informe
  /// primero como falla del propio tick, con su fase.
  /// </summary>
  public required TimeSpan TickStallTimeout { get; init; }

  /// <summary>
  /// Ejecuta Economy y Combat en paralelo (verdadero, lo aprobado) o en secuencia (falso, solo para medir y comparar en la
  /// defensa). El resultado tiene que ser idéntico en ambos modos (RECOMENDADO D-051).
  /// </summary>
  public bool RunDomainPhaseInParallel { get; init; } = true;

  public DiagnosticsLevel Diagnostics { get; init; } = DiagnosticsLevel.Summary;

  /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
  public IReadOnlyList<string> GetValidationErrors()
  {
    var errors = new List<string>();

    if (TickRate is null)
    {
      errors.Add($"{nameof(TickRate)} es obligatorio.");
    }

    if (MaxCommandsPerTick <= 0)
    {
      errors.Add($"{nameof(MaxCommandsPerTick)} debe ser positivo ({MaxCommandsPerTick}).");
    }

    if (CommandQueueCapacity < MaxCommandsPerTick)
    {
      errors.Add($"{nameof(CommandQueueCapacity)} ({CommandQueueCapacity}) no puede ser menor que {nameof(MaxCommandsPerTick)}.");
    }

    if (BarrierTimeout <= TimeSpan.Zero || (TickRate is not null && BarrierTimeout <= TickRate.TickInterval))
    {
      errors.Add($"{nameof(BarrierTimeout)} debe ser positivo y mayor que el intervalo de tick ({BarrierTimeout}).");
    }

    if (TickStallTimeout <= BarrierTimeout)
    {
      errors.Add($"{nameof(TickStallTimeout)} ({TickStallTimeout}) debe ser mayor que {nameof(BarrierTimeout)} ({BarrierTimeout}).");
    }

    if (!Enum.IsDefined(Diagnostics))
    {
      errors.Add($"{nameof(Diagnostics)} inválido ({Diagnostics}).");
    }

    return errors;
  }

  /// <summary>Valida la configuración completa.</summary>
  /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
  public void Validate()
  {
    IReadOnlyList<string> errors = GetValidationErrors();
    if (errors.Count > 0)
    {
      throw new ArgumentException("Configuración de Simulation inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
  }
}
