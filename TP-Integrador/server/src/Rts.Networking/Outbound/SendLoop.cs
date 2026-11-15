using System.Text;
using System.Text.Json;
using Rts.Protocol;
using Rts.Protocol.Outbound;

namespace Rts.Networking.Outbound;

/// <summary>
/// Escritor único de una conexión (contrato 08 §7, §9). Es el único consumidor de su <see cref="IOutboundQueue"/> y el único que
/// escribe en su socket: toma cada mensaje, arma el sobre, lo serializa con <see cref="ProtocolJson.Options"/>, le agrega <c>\n</c> y
/// lo escribe de una sola vez, para que ningún mensaje intercale sus bytes con otro.
/// <para>
/// Termina cuando la cola está completada y vacía (después de entregar lo pendiente) o al cancelarse el token, que es como el
/// cierre corta el socket al vencer <c>CloseLinger</c>.
/// </para>
/// </summary>
internal sealed class SendLoop
{
  private readonly IOutboundQueue _queue;
  private readonly Stream _stream;
  private readonly int _protocolVersion;

  public SendLoop(IOutboundQueue queue, Stream stream, int protocolVersion)
  {
    _queue = queue;
    _stream = stream;
    _protocolVersion = protocolVersion;
  }

  public async Task RunAsync(CancellationToken cancellationToken)
  {
    while (await _queue.DequeueAsync(cancellationToken).ConfigureAwait(false) is { } message)
    {
      await _stream.WriteAsync(ToLine(message), cancellationToken).ConfigureAwait(false);
    }
  }

  private byte[] ToLine(OutboundMessage message)
  {
    // Se serializa por el tipo real: como IOutboundPayload, el payload saldría como {}.
    var payload = JsonSerializer.SerializeToElement(message.Payload, message.Payload.GetType(), ProtocolJson.Options);
    var envelope = new NetworkEnvelope(_protocolVersion, message.Type, null, payload);
    return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, ProtocolJson.Options) + "\n");
  }
}
