using Rts.Shared.Errors;

namespace Rts.Networking.Admission;

/// <summary>
/// Contador de faltas de una conexión con <b>ventana deslizante</b> (DECIDIDO D-074): guarda el momento de cada falta y, al registrar
/// una nueva, olvida las que ya salieron de la ventana. Más de <c>maxViolations</c> faltas dentro de <c>window</c> pide cerrar la
/// conexión con <c>Network.AbuseDetected</c>.
/// <para>
/// Una falta registrada en el instante <c>t</c> cuenta mientras <c>ahora − t</c> sea menor que <c>window</c>: al cumplirse la ventana
/// exacta, ya no cuenta. No usa timers: el tiempo se mide con <see cref="TimeProvider"/> al registrar o consultar, así las pruebas
/// controlan el reloj.
/// </para>
/// <para>
/// Qué es una falta lo decide quien llama (el <c>InboundRouter</c>); acá solo se cuentan. Hay uno por conexión: registra solo su
/// receptor, pero <see cref="RecentCount"/> lo leen las métricas desde otro hilo, por eso todo pasa por un lock breve.
/// </para>
/// </summary>
internal sealed class ViolationTracker : IViolationTracker
{
  private readonly int _maxViolations;
  private readonly TimeSpan _window;
  private readonly TimeProvider _timeProvider;

  // Momentos de las faltas dentro de la ventana, de la más vieja a la más nueva.
  private readonly Queue<long> _timestamps = new();
  private readonly Lock _gate = new();

  public ViolationTracker(int maxViolations, TimeSpan window, TimeProvider timeProvider)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxViolations);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
    ArgumentNullException.ThrowIfNull(timeProvider);

    _maxViolations = maxViolations;
    _window = window;
    _timeProvider = timeProvider;
  }

  /// <inheritdoc/>
  public int RecentCount
  {
    get
    {
      lock (_gate)
      {
        ForgetExpired(_timeProvider.GetTimestamp());
        return _timestamps.Count;
      }
    }
  }

  /// <inheritdoc/>
  public bool Register(ErrorCode violation)
  {
    long now = _timeProvider.GetTimestamp();
    lock (_gate)
    {
      ForgetExpired(now);
      _timestamps.Enqueue(now);
      return _timestamps.Count > _maxViolations;
    }
  }

  // Debe llamarse con _gate tomado.
  private void ForgetExpired(long now)
  {
    while (_timestamps.TryPeek(out long oldest) && _timeProvider.GetElapsedTime(oldest, now) >= _window)
    {
      _timestamps.Dequeue();
    }
  }
}
