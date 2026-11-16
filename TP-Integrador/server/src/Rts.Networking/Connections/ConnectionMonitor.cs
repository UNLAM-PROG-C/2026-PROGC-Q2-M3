using Microsoft.Extensions.Logging;

namespace Rts.Networking.Connections;

/// <summary>
/// Un solo monitor periódico para todas las conexiones (contrato 08 §8, §9): cada <see cref="CheckPeriod"/> le pide a cada conexión
/// que revise sus plazos (<see cref="Connection.CheckTimeouts"/>). Usa un <see cref="PeriodicTimer"/> sobre el
/// <see cref="TimeProvider"/> del servidor y no depende de los ticks, así que funciona en todas las fases de la sesión.
/// <para>
/// Las decisiones de cada plazo viven en la conexión; el monitor solo marca el ritmo. Un plazo se detecta, como mucho,
/// <see cref="CheckPeriod"/> después de vencer. Un fallo inesperado al revisar una conexión queda en el log y no detiene al monitor.
/// </para>
/// </summary>
internal sealed class ConnectionMonitor
{
  /// <summary>Cada cuánto se revisan los plazos. Chico frente a los plazos (segundos) y barato: recorrer dos o tres conexiones.</summary>
  internal static readonly TimeSpan CheckPeriod = TimeSpan.FromMilliseconds(100);

  private readonly Func<IEnumerable<Connection>> _connections;
  private readonly TimeProvider _time;
  private readonly ILogger<ConnectionMonitor> _logger;

  public ConnectionMonitor(Func<IEnumerable<Connection>> connections, TimeProvider time, ILogger<ConnectionMonitor> logger)
  {
    _connections = connections;
    _time = time;
    _logger = logger;
  }

  /// <summary>Revisa los plazos hasta la cancelación.</summary>
  public async Task RunAsync(CancellationToken cancellationToken)
  {
    using var timer = new PeriodicTimer(CheckPeriod, _time);
    try
    {
      while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
      {
        CheckAll();
      }
    }
    catch (OperationCanceledException)
    {
      // Cancelación pedida: el servidor dejó de correr.
    }
  }

  private void CheckAll()
  {
    foreach (Connection connection in _connections())
    {
      try
      {
        connection.CheckTimeouts();
      }
      catch (Exception e)
      {
        _logger.LogError(e, "Fallo inesperado al revisar los plazos de la conexión {ConnectionId}", connection.Id);
      }
    }
  }
}
