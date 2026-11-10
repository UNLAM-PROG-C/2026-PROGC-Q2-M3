using Rts.Protocol.Outbound;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking;

/// <summary>Fase de una conexión desde el punto de vista de Networking. La plaza y el jugador los decide GameSession.</summary>
public enum ConnectionPhase
{
  /// <summary>Socket aceptado; esperando <c>Hello</c> dentro de <c>HandshakeTimeout</c>.</summary>
  AwaitingHello = 1,

  /// <summary>Hello válido informado a GameSession (D-053). Puede tener plaza o estar por recibir <c>Disconnecting(MatchFull)</c>.</summary>
  HelloAccepted = 2,

  /// <summary>Cierre en curso: no se lee más; se intenta entregar lo pendiente y el <c>Disconnecting</c>.</summary>
  Closing = 3,

  /// <summary>Socket cerrado y desconexión informada a GameSession (una sola vez).</summary>
  Closed = 4,
}

/// <summary>Métricas de una conexión, para medir y defender el diseño (08 §11).</summary>
public sealed record ConnectionMetricsSnapshot(
    ConnectionId Connection,
    PlayerId? Player,
    ConnectionPhase Phase,
    int OutboundDepth,
    long MessagesReceived,
    long MessagesSent,
    long BytesReceived,
    long BytesSent,
    long CommandsAdmitted,
    long CommandsRejected,
    long PositionsSuperseded,
    int RecentViolations);

/// <summary>
/// Servidor TCP de la partida (08 §1, §7). Implementa <see cref="IClientOutbox"/> para State Sync. Recibe en su construcción
/// <c>ISessionEventSink</c>, <c>ISessionGate</c>, <c>ICommandSink</c>, la configuración y un <see cref="TimeProvider"/>.
/// <para>Hilos (RECOMENDADO D-078), todo con I/O asincrónica, sin un hilo por mensaje:</para>
/// <list type="bullet">
/// <item>Un bucle de aceptación.</item>
/// <item>Por conexión: un receptor (<c>ReceiveLoop</c>) y un escritor (<c>SendLoop</c>), el único que escribe en ese socket.</item>
/// <item>Un monitor periódico de handshake y heartbeat para todas las conexiones.</item>
/// </list>
/// Ningún método bloquea al hilo que llama, y ninguno se llama desde dentro del tick salvo <see cref="IClientOutbox.Enqueue"/>, que
/// solo encola.
/// </summary>
public interface INetworkServer : IClientOutbox
{
  /// <summary>Escucha y acepta conexiones hasta la cancelación o hasta terminar <see cref="DrainAndCloseAllAsync"/>.</summary>
  Task RunAsync(CancellationToken cancellationToken);

  /// <summary>Codifica y encola un mensaje de sesión para la conexión. Si satura la cola, cierra la conexión.</summary>
  void Send(ConnectionId connection, ISessionMessage message);

  /// <summary>Encola <c>Disconnecting(reason)</c>, deja de leer y cierra al entregarlo o al vencer <c>CloseLinger</c>.</summary>
  void Close(ConnectionId connection, ErrorCode reason);

  /// <summary>
  /// Cierre de la partida (07 §9): deja de aceptar, encola <c>Disconnecting(Network.ServerShuttingDown)</c> en cada conexión, espera
  /// que las colas se vacíen hasta <c>DrainTimeout</c> y cierra todo. Termina aunque haya sockets muertos.
  /// </summary>
  Task DrainAndCloseAllAsync();

  /// <summary>Métricas actuales de todas las conexiones abiertas.</summary>
  IReadOnlyList<ConnectionMetricsSnapshot> GetMetrics();
}
