namespace Rts.Networking.Connections;

/// <summary>Qué tiene que hacer la conexión según su <see cref="Heartbeat"/>.</summary>
internal enum HeartbeatStatus
{
  /// <summary>Nada: hubo entrada reciente o el <c>Ping</c> pendiente todavía tiene tiempo.</summary>
  Alive = 1,

  /// <summary>Pasó <c>PingIdleInterval</c> sin entrada válida: hay que enviar un <c>Ping</c> con el id indicado.</summary>
  PingDue = 2,

  /// <summary>El <c>Ping</c> pendiente no se respondió dentro de <c>PongTimeout</c>: la conexión se da por perdida.</summary>
  TimedOut = 3,
}

/// <summary>
/// Estado del heartbeat de una conexión (módulo 08 §6, contrato 08 §8, RECOMENDADO D-079).
/// <list type="bullet">
/// <item>Si pasa <c>PingIdleInterval</c> sin <b>entrada válida</b> del cliente, pide enviar un <c>Ping</c> con un <c>pingId</c>
/// correlativo (1, 2, 3…). Enviar mensajes al cliente no cuenta como actividad.</item>
/// <item>Solo el <c>Pong</c> con el <c>pingId</c> pendiente lo da por respondido; uno con otro id se ignora, sin falta. Otro mensaje
/// válido reinicia la inactividad pero no responde el <c>Ping</c> pendiente: el <c>Pong</c> "debe corresponder al Ping pendiente".</item>
/// <item>Si el <c>Ping</c> pendiente no se responde dentro de <c>PongTimeout</c>, la conexión se da por perdida.</item>
/// </list>
/// Lo tocan dos hilos: el receptor (<see cref="RecordActivity"/>, <see cref="RecordPong"/>) y el monitor (<see cref="Check"/>). Por eso
/// todo pasa por un lock breve, que nunca se toma junto con otro.
/// </summary>
internal sealed class Heartbeat
{
  private readonly TimeSpan _idleInterval;
  private readonly TimeSpan _pongTimeout;
  private readonly TimeProvider _time;
  private readonly Lock _gate = new();

  private long _lastActivity;
  private long _lastPingId;
  private long? _pendingPingId;
  private long _pingSentAt;

  public Heartbeat(TimeSpan idleInterval, TimeSpan pongTimeout, TimeProvider time)
  {
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleInterval, TimeSpan.Zero);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pongTimeout, TimeSpan.Zero);
    ArgumentNullException.ThrowIfNull(time);

    _idleInterval = idleInterval;
    _pongTimeout = pongTimeout;
    _time = time;
    _lastActivity = time.GetTimestamp();
  }

  /// <summary>Llegó un mensaje válido del cliente: reinicia el tiempo de inactividad.</summary>
  public void RecordActivity()
  {
    long now = _time.GetTimestamp();
    lock (_gate)
    {
      _lastActivity = now;
    }
  }

  /// <summary>Llegó un <c>Pong</c>: si es el del <c>Ping</c> pendiente, lo da por respondido; si no, se ignora.</summary>
  public void RecordPong(long pingId)
  {
    lock (_gate)
    {
      if (_pendingPingId == pingId)
      {
        _pendingPingId = null;
      }
    }
  }

  /// <summary>Revisa los plazos con el reloj actual. Si devuelve <see cref="HeartbeatStatus.PingDue"/>, el <c>Ping</c> ya queda pendiente.</summary>
  public HeartbeatStatus Check(out long pingId)
  {
    long now = _time.GetTimestamp();
    pingId = 0;
    lock (_gate)
    {
      if (_pendingPingId is not null)
      {
        return _time.GetElapsedTime(_pingSentAt, now) >= _pongTimeout ? HeartbeatStatus.TimedOut : HeartbeatStatus.Alive;
      }

      if (_time.GetElapsedTime(_lastActivity, now) < _idleInterval)
      {
        return HeartbeatStatus.Alive;
      }

      pingId = ++_lastPingId;
      _pendingPingId = pingId;
      _pingSentAt = now;
      return HeartbeatStatus.PingDue;
    }
  }
}
