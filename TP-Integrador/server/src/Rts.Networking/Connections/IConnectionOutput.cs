using Rts.Protocol.Outbound;
using Rts.Shared.Errors;

namespace Rts.Networking.Connections;

/// <summary>
/// Lo que el receptor de una conexión puede hacer hacia afuera: responderle al cliente y cerrar la conexión. Lo implementa
/// <c>Connection</c>; así el <see cref="InboundRouter"/> se prueba sin sockets.
/// </summary>
internal interface IConnectionOutput
{
  /// <summary>Encola un mensaje para el cliente. No bloquea.</summary>
  void Send(OutboundMessage message);

  /// <summary>Cierra la conexión: encola <c>Disconnecting(reason)</c>, deja de leer y corta al entregarlo o al vencer <c>CloseLinger</c> (08 §7).</summary>
  void Close(ErrorCode reason);
}
