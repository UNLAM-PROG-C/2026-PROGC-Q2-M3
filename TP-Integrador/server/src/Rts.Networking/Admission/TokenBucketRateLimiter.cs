namespace Rts.Networking.Admission;

/// <summary>
/// Límite de comandos por segundo de una conexión, como <b>balde de fichas</b> (RECOMENDADO D-077): el balde parte lleno con
/// <c>capacity</c> fichas, cada comando gasta una y se recarga a <c>refillPerSecond</c> fichas por segundo sin pasar de
/// <c>capacity</c>. Sin fichas, el comando se rechaza y no se quita nada de lo ya admitido.
/// <para>
/// No usa timers: la recarga se calcula al pedir una ficha, con el tiempo que pasó desde la vez anterior (medido con
/// <see cref="TimeProvider"/>, así las pruebas controlan el reloj). Las fichas pueden ser fraccionarias, de modo que un intento
/// rechazado no pierde lo que ya se recargó.
/// </para>
/// <para>
/// <b>No es seguro para hilos a propósito:</b> hay uno por conexión y lo usa solo el receptor de esa conexión (08 §4).
/// </para>
/// </summary>
internal sealed class TokenBucketRateLimiter : ICommandRateLimiter
{
  /// <summary>Fichas que cuesta un comando.</summary>
  private const double TokensPerCommand = 1;

  private readonly int _capacity;
  private readonly int _refillPerSecond;
  private readonly TimeProvider _timeProvider;

  private double _tokens;
  private long _lastRefillTimestamp;

  public TokenBucketRateLimiter(int capacity, int refillPerSecond, TimeProvider timeProvider)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);
    ArgumentNullException.ThrowIfNull(timeProvider);

    _capacity = capacity;
    _refillPerSecond = refillPerSecond;
    _timeProvider = timeProvider;
    _tokens = capacity;
    _lastRefillTimestamp = timeProvider.GetTimestamp();
  }

  /// <inheritdoc/>
  public bool TryAcquire()
  {
    Refill();

    if (_tokens < TokensPerCommand)
    {
      return false;
    }

    _tokens -= TokensPerCommand;
    return true;
  }

  private void Refill()
  {
    long now = _timeProvider.GetTimestamp();
    TimeSpan elapsed = _timeProvider.GetElapsedTime(_lastRefillTimestamp, now);
    _lastRefillTimestamp = now;

    _tokens = Math.Min(_capacity, _tokens + (elapsed.TotalSeconds * _refillPerSecond));
  }
}
