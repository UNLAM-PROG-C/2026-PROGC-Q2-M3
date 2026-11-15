using Rts.Networking.Framing;
using Rts.Networking.Parsing;

namespace Rts.Networking.Connections;

/// <summary>
/// Receptor de una conexión (contrato 08 §4, §9). Lee el socket de forma asincrónica y une las piezas de entrada: los bytes pasan
/// por el <see cref="ILineFrameDecoder"/>, cada línea completa por el <see cref="IInboundMessageParser"/> y su resultado por el
/// <see cref="InboundRouter"/>.
/// <para>
/// Una línea que supera el máximo cierra la conexión con <c>Network.MessageTooLarge</c> en cuanto se detecta, sin esperar el
/// <c>\n</c> (D-074). Termina cuando el cliente cierra su lado (lectura de 0 bytes), después de <c>Oversize</c> o al cancelarse el
/// token, que es como el cierre deja de leer. Hay una por conexión y no comparte nada con las demás.
/// </para>
/// </summary>
internal sealed class ReceiveLoop
{
  private const int ReadBufferBytes = 4096;

  private readonly ILineFrameDecoder _decoder;
  private readonly IInboundMessageParser _parser;
  private readonly InboundRouter _router;
  private readonly IConnectionOutput _output;

  public ReceiveLoop(ILineFrameDecoder decoder, IInboundMessageParser parser, InboundRouter router, IConnectionOutput output)
  {
    _decoder = decoder;
    _parser = parser;
    _router = router;
    _output = output;
  }

  public async Task RunAsync(Stream stream, CancellationToken cancellationToken)
  {
    byte[] buffer = new byte[ReadBufferBytes];

    while (true)
    {
      int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
      if (read == 0)
      {
        return;
      }

      _decoder.Append(buffer.AsSpan(0, read));

      if (!RouteCompleteLines(cancellationToken))
      {
        return;
      }
    }
  }

  /// <summary>Procesa todas las líneas completas que hay. Devuelve <c>false</c> si hay que dejar de leer.</summary>
  private bool RouteCompleteLines(CancellationToken cancellationToken)
  {
    while (true)
    {
      // Si un mensaje anterior cerró la conexión, las líneas que quedaban en el buffer ya no se procesan.
      cancellationToken.ThrowIfCancellationRequested();

      FrameReadResult next = _decoder.ReadNext();
      switch (next.Status)
      {
        case FrameStatus.NeedMoreData:
          return true;
        case FrameStatus.Oversize:
          _output.Close(NetworkErrors.MessageTooLarge);
          return false;
        default:
          _router.Handle(_parser.Parse(next.Frame.Span));
          break;
      }
    }
  }
}
