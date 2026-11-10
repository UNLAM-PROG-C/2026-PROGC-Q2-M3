using System.Text.Json;

namespace Rts.Protocol;

/// <summary>
/// Sobre de todo mensaje, en ambos sentidos (08 §3, RECOMENDADO D-076). Una línea JSON por mensaje:
/// <code>{"protocolVersion":1,"type":"Move","messageId":42,"payload":{...}}</code>
/// <list type="bullet">
/// <item><see cref="ProtocolVersion"/>: obligatorio en cada mensaje (08 §2 pide validar la versión en la recepción). Un <c>Hello</c>
/// con otra versión se rechaza y se cierra; cualquier otro mensaje con otra versión es una falta.</item>
/// <item><see cref="Type"/>: uno de <see cref="ProtocolMessageTypes"/>.</item>
/// <item><see cref="MessageId"/>: obligatorio en gameplay, donde es el <c>ClientCommandId</c> en [1, 2^53] (D-005). Opcional en el resto;
/// el servidor no lo usa en sus mensajes.</item>
/// <item><see cref="Payload"/>: objeto del DTO correspondiente al tipo. Puede faltar en mensajes sin datos.</item>
/// </list>
/// </summary>
public sealed record NetworkEnvelope(int ProtocolVersion, string Type, long? MessageId, JsonElement? Payload)
{
  /// <summary>Versión inicial del protocolo. El valor vigente es configurable en Networking (08 §10).</summary>
  public const int InitialVersion = 1;
}
