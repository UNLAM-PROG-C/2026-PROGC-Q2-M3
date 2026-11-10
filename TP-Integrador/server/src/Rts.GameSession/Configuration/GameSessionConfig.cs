namespace Rts.GameSession.Configuration;

/// <summary>
/// Parámetros de GameSession (07 §11). La espera del segundo jugador es ilimitada y no tiene parámetro (DECIDIDO). El plazo de
/// drenaje al cerrar pertenece a Networking (07 §9). Valores PENDIENTES; se valida una vez al iniciar el servidor.
/// </summary>
public sealed record GameSessionConfig
{
  /// <summary>Plazo para recibir ambos <c>ClientReady</c> desde que se entra a Starting, medido con reloj monotónico (07 §4).</summary>
  public required TimeSpan ReadyTimeout { get; init; }

  /// <summary>
  /// Capacidad de la cola serializada de eventos de sesión. Solo limita <c>Hello</c> y <c>ClientReady</c>: los cierres de conexión y
  /// los avisos internos (timer, Simulation, drenaje) nunca se descartan (D-059).
  /// </summary>
  public required int EventQueueCapacity { get; init; }

  /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
  public IReadOnlyList<string> GetValidationErrors()
  {
    var errors = new List<string>();

    if (ReadyTimeout <= TimeSpan.Zero)
    {
      errors.Add($"{nameof(ReadyTimeout)} debe ser positivo ({ReadyTimeout}).");
    }

    if (EventQueueCapacity <= 0)
    {
      errors.Add($"{nameof(EventQueueCapacity)} debe ser positivo ({EventQueueCapacity}).");
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
      throw new ArgumentException("Configuración de GameSession inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
  }
}
