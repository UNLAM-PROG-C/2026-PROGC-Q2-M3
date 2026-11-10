using Rts.Protocol.Outbound;

namespace Rts.Protocol.Transport;

// DTOs de transporte y conectividad (contrato 08).

/// <summary>Ambos sentidos. Quien lo recibe responde <see cref="PongDto"/> con el mismo <see cref="PingId"/>.</summary>
public sealed record PingDto(long PingId) : IOutboundPayload;

public sealed record PongDto(long PingId) : IOutboundPayload;

/// <summary>
/// DECIDIDO (D-075): un comando de gameplay que Networking rechazó <b>antes</b> de admitirlo (fuera de Running, límite por segundo,
/// cola global llena, estructura inválida). No es un resultado oficial: el comando nunca entró a Simulation y puede reintentarse
/// con el mismo <see cref="MessageId"/>.
/// </summary>
public sealed record CommandRejectedDto(long MessageId, string Error) : IOutboundPayload;

/// <summary>
/// Un mensaje no se pudo procesar (08 §2). La conexión sigue abierta salvo que se indique lo contrario con <c>Disconnecting</c>.
/// <see cref="ServerProtocolVersion"/> solo acompaña a <c>Network.UnsupportedVersion</c>.
/// </summary>
public sealed record ProtocolErrorDto(string Error, long? MessageId, string? Detail, int? ServerProtocolVersion) : IOutboundPayload;

/// <summary>Último mensaje antes de que el servidor cierre la conexión, con el motivo como <c>ErrorCode</c> (contrato 07 §4).</summary>
public sealed record DisconnectingDto(string Reason) : IOutboundPayload;
