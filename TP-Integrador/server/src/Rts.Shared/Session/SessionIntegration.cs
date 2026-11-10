using Rts.Shared.Ids;

namespace Rts.Shared.Session;

/// <summary>Estados DECIDIDOS de la sesión (07 §3). El valor 0 queda sin definir a propósito.</summary>
public enum SessionStatus
{
  /// <summary>Se espera completar dos clientes. Sin ticks.</summary>
  WaitingForPlayers = 1,

  /// <summary>Ambos registrados; InitialState enviado; se esperan los dos Ready válidos. Sin ticks.</summary>
  Starting = 2,

  /// <summary>Ambos listos; Simulation ejecuta ticks.</summary>
  Running = 3,

  /// <summary>Resultado fijado; se envía MatchEnded y se drenan las conexiones.</summary>
  Ending = 4,

  /// <summary>Terminal: recursos liberados; el proceso termina (D-058).</summary>
  Ended = 5,
}

/// <summary>
/// Entrada de eventos de sesión que usa Networking (07 §7, 08 §4), sin referenciar el proyecto GameSession.
/// La implementación (GameSession) solo encola: nunca bloquea al receptor TCP ni muta estado en el hilo que llama.
/// Todos los eventos se serializan en un único consumidor.
/// </summary>
public interface ISessionEventSink
{
  /// <summary>
  /// La conexión envió un <c>Hello</c> con versión de protocolo compatible (Networking ya la validó). Recién ahí puede ocupar
  /// plaza (DECIDIDO D-053). Networking lo informa como máximo una vez por conexión.
  /// </summary>
  /// <returns><c>false</c> si la cola de sesión está llena; Networking puede cerrar la conexión.</returns>
  bool TryPostHello(ConnectionId connection);

  /// <summary>La conexión envió <c>ClientReady</c> con ese <see cref="InitializationId"/>; GameSession lo valida.</summary>
  /// <returns><c>false</c> si la cola de sesión está llena; Networking responde ocupado y el cliente puede reintentar.</returns>
  bool TryPostClientReady(ConnectionId connection, InitializationId initialization);

  /// <summary>
  /// La conexión se cerró o se perdió (cierre del socket, timeout de heartbeat, saturación). Se informa exactamente una vez
  /// por conexión y <b>nunca se descarta</b>, aunque la cola esté llena (07 §5).
  /// </summary>
  void PostConnectionClosed(ConnectionId connection);
}

/// <summary>
/// Vista de lectura de la sesión para Networking, segura para hilos: la respalda una instantánea inmutable que GameSession
/// reemplaza después de cada transición. Networking la consulta en sus receptores concurrentes.
/// </summary>
public interface ISessionGate
{
  /// <summary>Estado actual de la sesión.</summary>
  SessionStatus Status { get; }

  /// <summary>
  /// Verdadero solo en Running y antes de que GameSession pida detener los ticks. Si es falso, Networking rechaza los comandos de
  /// gameplay con <c>Common.MatchNotRunning</c> antes de admitirlos (D-045).
  /// </summary>
  bool AcceptsGameplay { get; }

  /// <summary>
  /// <see cref="PlayerId"/> asignado a una conexión registrada y conectada, o <c>null</c>. Es la <b>única</b> fuente del
  /// <c>PlayerId</c> de un comando: nunca se confía en un campo enviado por el cliente.
  /// </summary>
  PlayerId? GetPlayer(ConnectionId connection);

  /// <summary>
  /// Conexión registrada y conectada de un jugador, o <c>null</c>. Networking la usa para dirigir las publicaciones que State Sync
  /// encola por <see cref="PlayerId"/> (D-069); si es <c>null</c>, el mensaje se descarta: no hay entrega a sockets muertos.
  /// </summary>
  ConnectionId? GetConnection(PlayerId player);
}
