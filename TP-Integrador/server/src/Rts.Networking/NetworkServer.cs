using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Rts.Networking.Configuration;
using Rts.Networking.Connections;
using Rts.Networking.Outbound;
using Rts.Protocol.Outbound;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking;

/// <summary>
/// Servidor TCP de la partida (contrato 08 §1, §3, §9; contrato 10 §4, paso 3). Acepta conexiones y le da a cada una su
/// <see cref="Connection"/>, con su receptor y su escritor. Implementa <see cref="IClientOutbox"/> para State Sync.
/// <list type="bullet">
/// <item>Cada conexión recibe un <see cref="ConnectionId"/> creciente y se registra mientras vive. Cuando termina se quita del registro y se
/// cierra su socket.</item>
/// <item><see cref="Send"/> y <see cref="Enqueue"/> solo encolan, y descartan el mensaje si la conexión ya no existe (09 D-069).</item>
/// <item>Si ya hay <c>MaxPendingConnections</c> conexiones esperando su <c>Hello</c>, la nueva se acepta solo para cerrarla con
/// <c>Disconnecting(TooManyConnections)</c> (08 §3). Como toda conexión aceptada, se informa cerrada a GameSession.</item>
/// <item>Al cancelar <see cref="RunAsync"/> deja de aceptar; cerrar las conexiones existentes es <see cref="DrainAndCloseAllAsync"/>.</item>
/// </list>
/// PENDIENTE (fase 3): <see cref="DrainAndCloseAllAsync"/> y <see cref="GetMetrics"/>.
/// </summary>
public sealed class NetworkServer : INetworkServer
{
  private readonly NetworkingConfig _config;
  private readonly ISessionEventSink _sessionEvents;
  private readonly ISessionGate _gate;
  private readonly ICommandSink _commands;
  private readonly TimeProvider _time;
  private readonly ILogger<NetworkServer> _logger;
  private readonly ILogger<Connection> _connectionLogger;
  private readonly SessionMessageEncoder _encoder = new();
  private readonly ConcurrentDictionary<ConnectionId, Connection> _connections = new();
  private readonly TaskCompletionSource<int> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private long _lastConnectionId;

  public NetworkServer(
      NetworkingConfig config,
      ISessionEventSink sessionEvents,
      ISessionGate gate,
      ICommandSink commands,
      TimeProvider time,
      ILoggerFactory loggers)
  {
    _config = config;
    _sessionEvents = sessionEvents;
    _gate = gate;
    _commands = commands;
    _time = time;
    _logger = loggers.CreateLogger<NetworkServer>();
    _connectionLogger = loggers.CreateLogger<Connection>();
  }

  /// <summary>Puerto en el que escucha, disponible cuando <see cref="RunAsync"/> ya empezó a escuchar (útil con el puerto 0 en pruebas).</summary>
  internal Task<int> ListeningPort => _listening.Task;

  public async Task RunAsync(CancellationToken cancellationToken)
  {
    var listener = new TcpListener(IPAddress.Parse(_config.ListenAddress), _config.Port);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    _listening.TrySetResult(port);
    _logger.LogInformation("Networking escuchando en {Address}:{Port}", _config.ListenAddress, port);

    try
    {
      while (true)
      {
        TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        Accept(client);
      }
    }
    catch (OperationCanceledException)
    {
      // Cancelación pedida: se deja de aceptar.
    }
    finally
    {
      listener.Stop();
    }
  }

  public void Send(ConnectionId connection, ISessionMessage message)
  {
    OutboundMessage encoded = _encoder.Encode(message);
    if (_connections.TryGetValue(connection, out Connection? target))
    {
      target.Send(encoded);
    }
  }

  public void Close(ConnectionId connection, ErrorCode reason)
  {
    if (_connections.TryGetValue(connection, out Connection? target))
    {
      target.Close(reason);
    }
  }

  public void Enqueue(PlayerId recipient, OutboundMessage message)
  {
    // Sin conexión registrada para ese jugador, o ya cerrada, no hay entrega a sockets muertos (09 D-069).
    if (_gate.GetConnection(recipient) is { } connection && _connections.TryGetValue(connection, out Connection? target))
    {
      target.Send(message);
    }
  }

  public Task DrainAndCloseAllAsync() => throw new NotImplementedException("PENDIENTE fase 3: drenaje final (contrato 08 §7).");

  public IReadOnlyList<ConnectionMetricsSnapshot> GetMetrics() => throw new NotImplementedException("PENDIENTE fase 3: métricas.");

  private void Accept(TcpClient client)
  {
    var id = new ConnectionId(Interlocked.Increment(ref _lastConnectionId));
    var connection = new Connection(id, client.GetStream(), _config, _sessionEvents, _gate, _commands, _time, _connectionLogger);

    // Se cuenta antes de registrar la nueva. Solo este bucle agrega conexiones, así que dos aceptaciones no pueden pasar el tope a la vez.
    bool tooManyPending = CountPending() >= _config.MaxPendingConnections;
    _connections[id] = connection;
    _logger.LogInformation("Conexión {ConnectionId} aceptada", id);

    if (tooManyPending)
    {
      connection.Close(NetworkErrors.TooManyConnections);
    }

    _ = RunConnectionAsync(connection, client);
  }

  private int CountPending() => _connections.Values.Count(connection => connection.Phase == ConnectionPhase.AwaitingHello);

  private async Task RunConnectionAsync(Connection connection, TcpClient client)
  {
    try
    {
      await connection.RunAsync().ConfigureAwait(false);
    }
    catch (Exception e)
    {
      // Un fallo inesperado (un bug) en una conexión no tira al servidor: la conexión ya se cerró y se informó, y queda en el log.
      _logger.LogError(e, "Fallo inesperado en la conexión {ConnectionId}", connection.Id);
    }
    finally
    {
      _connections.TryRemove(connection.Id, out _);
      client.Dispose();
    }
  }
}
