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
/// <item><b>Saturación:</b> si un mensaje no entra en la cola de salida, se cierra con <c>Network.OutboundSaturated</c> (D-073).</item>
/// </list>
/// No es dueña del <see cref="Stream"/>: quien la creó lo cierra cuando <see cref="RunAsync"/> termina.
/// </summary>
internal sealed class Connection : IConnectionOutput
{
  private readonly ConnectionId _id;
  private readonly ISessionEventSink _session;
  private readonly TimeProvider _time;
  private readonly ILogger<Connection> _logger;
  private readonly TimeSpan _closeLinger;
  private readonly Stream _stream;
  private readonly OutboundQueue _queue;
  private readonly SendLoop _sendLoop;
  private readonly ReceiveLoop _receiveLoop;
  private readonly CancellationTokenSource _receiveCancellation = new();
  private readonly CancellationTokenSource _sendCancellation = new();
  private int _closing;

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
    _time = time;
    _logger = logger;
    _closeLinger = config.CloseLinger;

    _queue = new OutboundQueue(config.OutboundQueueCapacity);
    _sendLoop = new SendLoop(_queue, stream, config.ProtocolVersion);

    var rateLimiter = new TokenBucketRateLimiter(config.CommandBurst, config.CommandsPerSecond, time);
    var admitter = new CommandAdmitter(id, gate, rateLimiter, commands);
    var router = new InboundRouter(id, config.ProtocolVersion, session, admitter, this);
    _receiveLoop = new ReceiveLoop(
        new LineFrameDecoder(config.MaxInboundMessageBytes),
        new InboundMessageParser(config.ProtocolVersion),
        router,
        this);
  }

  public ConnectionId Id => _id;

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
      _session.PostConnectionClosed(_id);
    }
  }

  /// <summary>Encola un mensaje para el cliente. No bloquea. Si un mensaje no entra, cierra la conexión por saturación (D-073).</summary>
  public void Send(OutboundMessage message)
  {
    if (_queue.TryEnqueue(message) == EnqueueOutcome.Saturated)
    {
      Close(NetworkErrors.OutboundSaturated);
    }
  }

  /// <summary>Cierre individual (08 §7). Solo el primer llamado tiene efecto.</summary>
  public void Close(ErrorCode reason)
  {
    if (Interlocked.Exchange(ref _closing, 1) == 1)
    {
      return;
    }

    _logger.LogInformation("Conexión {ConnectionId} se cierra: {Reason}", _id, reason);

    // El Disconnecting va último: lo que ya estaba encolado se entrega antes.
    _queue.TryEnqueue(new OutboundMessage(ProtocolMessageTypes.Disconnecting, new DisconnectingDto(reason.Value), DeliveryClass.Essential));
    _queue.Complete();
    _receiveCancellation.Cancel();
    _ = CutAfterLingerAsync();
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
