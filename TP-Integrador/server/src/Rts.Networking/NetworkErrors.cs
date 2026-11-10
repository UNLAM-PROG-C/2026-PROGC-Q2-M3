using Rts.Shared.Errors;

namespace Rts.Networking;

/// <summary>
/// Códigos del área Network (D-007). Los de mensaje viajan en <c>ProtocolError</c> o <c>CommandRejected</c>; los de cierre, en
/// <c>Disconnecting</c>. Una estructura de gameplay inválida usa <c>Common.InvalidCommand</c>; fuera de Running,
/// <c>Common.MatchNotRunning</c>; cola global llena, <c>Common.CommandQueueFull</c>.
/// </summary>
public static class NetworkErrors
{
  // Mensajes inválidos: ProtocolError, y cuentan como falta (D-074).

  /// <summary>La línea no es UTF-8 válido.</summary>
  public static readonly ErrorCode InvalidUtf8 = new("Network.InvalidUtf8");

  /// <summary>La línea no es un objeto JSON.</summary>
  public static readonly ErrorCode MalformedJson = new("Network.MalformedJson");

  /// <summary>Faltan o son inválidos <c>protocolVersion</c> o <c>type</c>.</summary>
  public static readonly ErrorCode InvalidEnvelope = new("Network.InvalidEnvelope");

  /// <summary>Versión distinta de la del servidor. En un <c>Hello</c>, además, se cierra la conexión (08 §2).</summary>
  public static readonly ErrorCode UnsupportedVersion = new("Network.UnsupportedVersion");

  /// <summary>Tipo que el cliente no puede enviar.</summary>
  public static readonly ErrorCode UnknownType = new("Network.UnknownType");

  /// <summary>Gameplay sin <c>messageId</c>, o fuera de [1, 2^53] (D-005).</summary>
  public static readonly ErrorCode InvalidMessageId = new("Network.InvalidMessageId");

  /// <summary>Payload de un mensaje que no es de gameplay con estructura inválida.</summary>
  public static readonly ErrorCode InvalidPayload = new("Network.InvalidPayload");

  /// <summary>Cualquier mensaje distinto de <c>Hello</c>, <c>Ping</c> o <c>Pong</c> antes del <c>Hello</c>.</summary>
  public static readonly ErrorCode HelloRequired = new("Network.HelloRequired");

  /// <summary>Segundo <c>Hello</c> en la misma conexión.</summary>
  public static readonly ErrorCode DuplicateHello = new("Network.DuplicateHello");

  // Rechazos de comandos antes de admitir: CommandRejected (D-075).

  /// <summary>Superó el límite de comandos por segundo (08 §4). Cuenta como falta (D-074).</summary>
  public static readonly ErrorCode RateLimited = new("Network.RateLimited");

  /// <summary>La conexión no tiene jugador asignado.</summary>
  public static readonly ErrorCode NotRegistered = new("Network.NotRegistered");

  /// <summary>La cola de sesión está llena y no aceptó el Hello o el Ready; el cliente puede reintentar.</summary>
  public static readonly ErrorCode SessionBusy = new("Network.SessionBusy");

  // Motivos de cierre: Disconnecting.

  /// <summary>Un mensaje superó <c>MaxInboundMessageBytes</c>: cierre inmediato (D-074).</summary>
  public static readonly ErrorCode MessageTooLarge = new("Network.MessageTooLarge");

  /// <summary>Superó <c>MaxViolations</c> faltas dentro de <c>ViolationWindow</c> (D-074).</summary>
  public static readonly ErrorCode AbuseDetected = new("Network.AbuseDetected");

  /// <summary>No envió un <c>Hello</c> dentro de <c>HandshakeTimeout</c>.</summary>
  public static readonly ErrorCode HandshakeTimeout = new("Network.HandshakeTimeout");

  /// <summary>No respondió el <c>Ping</c> dentro de <c>PongTimeout</c> (08 §6).</summary>
  public static readonly ErrorCode HeartbeatTimeout = new("Network.HeartbeatTimeout");

  /// <summary>Un mensaje esencial no entró en la cola de salida (D-073).</summary>
  public static readonly ErrorCode OutboundSaturated = new("Network.OutboundSaturated");

  /// <summary>Ya hay <c>MaxPendingConnections</c> conexiones esperando su <c>Hello</c>.</summary>
  public static readonly ErrorCode TooManyConnections = new("Network.TooManyConnections");

  /// <summary>Cierre normal del servidor al terminar la partida (D-058).</summary>
  public static readonly ErrorCode ServerShuttingDown = new("Network.ServerShuttingDown");
}
