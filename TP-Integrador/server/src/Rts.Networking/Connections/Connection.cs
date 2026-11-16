using Microsoft.Extensions.Logging;
using Rts.Networking.Admission;
using Rts.Networking.Configuration;
using Rts.Networking.Framing;
using Rts.Networking.Outbound;
using Rts.Networking.Parsing;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Transport;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking.Connections;

/// <summary>
/// Una conexión de cliente (contrato 08 §3, §7, §9): une su cola de salida, su <see cref="SendLoop"/> (único escritor) y su
/// <see cref="ReceiveLoop"/>, y se ocupa de cómo termina.
/// <list type="bullet">
/// <item><b>Cierre único:</b> toda conexión termina en <see cref="RunAsync"/> y se informa con
/// <see cref="ISessionEventSink.PostConnectionClosed"/> <b>exactamente una vez</b>, sea cual sea la causa: el cliente cerró, falló el socket o
/// se pidió <see cref="Close"/>.</item>
/// <item><b><see cref="Close"/>:</b> encola <c>Disconnecting(reason)</c> detrás de lo ya encolado, completa la cola, deja de leer y corta el
/// socket al entregar todo o al vencer <c>CloseLinger</c> (08 §7).</item>
/// <item><b>Cliente ido</b> (cierre o error de socket sin <see cref="Close"/>): no se entrega nada pendiente, porque no hay entrega a
/// sockets muertos.</item>
/// <item><b>Saturación:</b> si un mensaje no entra en la cola de salida, se cierra con <c>Network.OutboundSaturated</c> (D-073): se
/// descartan los pendientes y se intenta enviar solo el <c>Disconnecting</c>, con <c>CloseLinger</c> como plazo (08 §7).</item>
/// </list>
/// No es dueña del <see cref="Stream"/>: quien la creó lo cierra cuando <see cref="RunAsync"/> termina.
/// </summary>
internal sealed class Connection : IConnectionOutput
{
  private readonly ConnectionId _id;
  private readonly ISessionEventSink _session;
  private readonly ISessionGate _gate;
  private readonly ConnectionCounters _counters = new();
  private readonly ViolationTracker _violations;
  private readonly TimeProvider _time;
  private readonly ILogger<Connection> _logger;
  private readonly TimeSpan _closeLinger;
  private readonly TimeSpan _handshakeTimeout;
  private readonly long _acceptedAt;
  private readonly Stream _stream;
  private readonly OutboundQueue _queue;
  private readonly SendLoop _sendLoop;
  private readonly ReceiveLoop _receiveLoop;
  private readonly InboundRouter _router;
  private readonly Heartbeat _heartbeat;
  private readonly CancellationTokenSource _receiveCancellation = new();
  private readonly CancellationTokenSource _sendCancellation = new();
  private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private int _closing;
  private int _closed;

  public Connection(
      ConnectionId id,
      Stream stream,
      NetworkingConfig config,
      ISessionEventSink session,
      ISessionGate gate,
      ICommandSink commands,
      TimeProvider time,
      ILogger<Connection> logger)
  {
    _id = id;
    _stream = stream;
    _session = session;
    _gate = gate;
    _time = time;
    _logger = logger;
    _closeLinger = config.CloseLinger;
    _handshakeTimeout = config.HandshakeTimeout;
    _acceptedAt = time.GetTimestamp();

    _queue = new OutboundQueue(config.OutboundQueueCapacity);
    _sendLoop = new SendLoop(_queue, stream, config.ProtocolVersion, _counters);

    var rateLimiter = new TokenBucketRateLimiter(config.CommandBurst, config.CommandsPerSecond, time);
    var admitter = new CommandAdmitter(id, gate, rateLimiter, commands);
    _violations = new ViolationTracker(config.MaxViolations, config.ViolationWindow, time);
    _heartbeat = new Heartbeat(config.PingIdleInterval, config.PongTimeout, time);
    _router = new InboundRouter(id, config.ProtocolVersion, session, admitter, _violations, _heartbeat, _counters, this);
    _receiveLoop = new ReceiveLoop(
        new LineFrameDecoder(config.MaxInboundMessageBytes),
        new InboundMessageParser(config.ProtocolVersion),
        _router,
        this,
        _counters);
  }

  public ConnectionId Id => _id;

  /// <summary>Métricas actuales de la conexión (08 §11). Se puede llamar desde cualquier hilo.</summary>
  public ConnectionMetricsSnapshot GetMetrics() => new(
      _id,
      _gate.GetPlayer(_id),
      Phase,
      _queue.Count,
      _counters.MessagesReceived,
      _counters.MessagesSent,
      _counters.BytesReceived,
      _counters.BytesSent,
      _counters.CommandsAdmitted,
      _counters.CommandsRejected,
      _counters.PositionsSuperseded,
      _violations.RecentCount);

  /// <summary>Termina cuando la conexión ya se cerró y se informó a GameSession (al final de <see cref="RunAsync"/>).</summary>
  public Task Completion => _completion.Task;

  /// <summary>Fase de la conexión (08 §3). Se puede leer desde cualquier hilo.</summary>
  public ConnectionPhase Phase =>
      Volatile.Read(ref _closed) == 1 ? ConnectionPhase.Closed
      : IsClosing ? ConnectionPhase.Closing
      : _router.HelloAccepted ? ConnectionPhase.HelloAccepted
      : ConnectionPhase.AwaitingHello;

  private bool IsClosing => Volatile.Read(ref _closing) == 1;

  /// <summary>Corre el receptor y el escritor hasta que la conexión termina, y la informa cerrada. Se llama una sola vez.</summary>
  public async Task RunAsync()
  {
    try
    {
      Task receive = ReceiveAsync();
      Task send = SendAsync();
      Task first = await Task.WhenAny(receive, send).ConfigureAwait(false);

      if (first == send)
      {
        // Se entregó todo lo pendiente o falló la escritura: ya no se lee.
        _receiveCancellation.Cancel();
      }
      else if (!IsClosing)
      {
        // El cliente se fue sin que se pidiera el cierre: no se entrega nada a un socket muerto.
        _sendCancellation.Cancel();
      }

      await Task.WhenAll(receive, send).ConfigureAwait(false);
    }
    finally
    {
      _queue.Complete();
      _sendCancellation.Cancel();
      _logger.LogInformation("Conexión {ConnectionId} cerrada ({Cause})", _id, IsClosing ? "por el servidor" : "el cliente se fue");
      Volatile.Write(ref _closed, 1);
      _session.PostConnectionClosed(_id);
      _completion.TrySetResult();
    }
  }

  /// <summary>
  /// Revisa los plazos de la conexión con el reloj actual; lo llama el <see cref="ConnectionMonitor"/> en cada vuelta.
  /// <list type="bullet">
  /// <item>Sin <c>Hello</c> dentro de <c>HandshakeTimeout</c> desde que se aceptó: <c>Disconnecting(HandshakeTimeout)</c> (08 §3).</item>
  /// <item>Heartbeat (08 §8): tras <c>PingIdleInterval</c> sin entrada válida, encola un <c>Ping</c>; si no se responde dentro de
  /// <c>PongTimeout</c>, <c>Disconnecting(HeartbeatTimeout)</c>. El cierre se informa a GameSession como cualquier otro.</item>
  /// </list>
  /// Una conexión que ya se está cerrando no se revisa.
  /// </summary>
  public void CheckTimeouts()
  {
    ConnectionPhase phase = Phase;
    if (phase is ConnectionPhase.Closing or ConnectionPhase.Closed)
    {
      return;
    }

    if (phase == ConnectionPhase.AwaitingHello && _time.GetElapsedTime(_acceptedAt, _time.GetTimestamp()) >= _handshakeTimeout)
    {
      Close(NetworkErrors.HandshakeTimeout);
      return;
    }

    switch (_heartbeat.Check(out long pingId))
    {
      case HeartbeatStatus.PingDue:
        Send(new OutboundMessage(ProtocolMessageTypes.Ping, new PingDto(pingId), DeliveryClass.Essential));
        break;
      case HeartbeatStatus.TimedOut:
        Close(NetworkErrors.HeartbeatTimeout);
        break;
    }
  }

  /// <summary>
  /// Encola un mensaje para el cliente. No bloquea. Si un mensaje no entra, cierra la conexión por saturación (D-073): se descartan
  /// los pendientes y se intenta enviar solo el <c>Disconnecting</c> (08 §7).
  /// </summary>
  public void Send(OutboundMessage message)
  {
    switch (_queue.TryEnqueue(message))
    {
      case EnqueueOutcome.Superseded:
        _counters.CountPositionSuperseded();
        break;
      case EnqueueOutcome.Saturated:
        Close(NetworkErrors.OutboundSaturated, discardPending: true);
        break;
    }
  }

  /// <summary>Cierre individual (08 §7): lo ya encolado se entrega antes del <c>Disconnecting</c>. Solo el primer llamado tiene efecto.</summary>
  public void Close(ErrorCode reason) => Close(reason, discardPending: false);

  private void Close(ErrorCode reason, bool discardPending)
  {
    if (Interlocked.Exchange(ref _closing, 1) == 1)
    {
      return;
    }

    _logger.LogInformation("Conexión {ConnectionId} se cierra: {Reason}", _id, reason);

    var farewell = new OutboundMessage(ProtocolMessageTypes.Disconnecting, new DisconnectingDto(reason.Value), DeliveryClass.Essential);
    if (discardPending)
    {
      _queue.DiscardPendingAndComplete(farewell);
    }
    else
    {
      // El Disconnecting va último: lo que ya estaba encolado se entrega antes.
      _queue.TryEnqueue(farewell);
      _queue.Complete();
    }

    _receiveCancellation.Cancel();
    _ = CutAfterLingerAsync();
  }

  /// <summary>Corta ya: deja de leer y de escribir sin esperar lo pendiente. Lo usa el drenaje cuando vence <c>DrainTimeout</c>.</summary>
  public void Abort()
  {
    _receiveCancellation.Cancel();
    _sendCancellation.Cancel();
  }

  private async Task CutAfterLingerAsync()
  {
    try
    {
      await Task.Delay(_closeLinger, _time, _sendCancellation.Token).ConfigureAwait(false);
      _sendCancellation.Cancel();
    }
    catch (OperationCanceledException)
    {
      // La conexión terminó antes de que venciera CloseLinger.
    }
  }

  // Un socket que se cierra o falla es parte normal de la vida de una conexión (08 §3), no un error del servidor.
  private async Task ReceiveAsync()
  {
    try
    {
      await _receiveLoop.RunAsync(_stream, _receiveCancellation.Token).ConfigureAwait(false);
    }
    catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
    {
    }
  }

  private async Task SendAsync()
  {
    try
    {
      await _sendLoop.RunAsync(_sendCancellation.Token).ConfigureAwait(false);
    }
    catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
    {
    }
  }
}
