using System.Net.Sockets;
using System.Text;
using Rts.Networking.Configuration;
using Rts.Networking.Tests.Admission;
using Rts.Networking.Tests.Connections;
using Rts.Shared.Commands;

namespace Rts.Networking.Tests;

/// <summary>Cliente TCP de prueba: habla por líneas, como Godot.</summary>
internal sealed class TestClient : IDisposable
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  private readonly TcpClient _client;
  private readonly StreamReader _reader;
  private readonly StreamWriter _writer;

  private TestClient(TcpClient client)
  {
    _client = client;
    NetworkStream stream = client.GetStream();
    _reader = new StreamReader(stream, new UTF8Encoding(false));
    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
  }

  public static async Task<TestClient> ConnectAsync(int port)
  {
    var client = new TcpClient();
    await client.ConnectAsync("127.0.0.1", port).WaitAsync(Patience);
    return new TestClient(client);
  }

  public Task SendLineAsync(string line) => _writer.WriteLineAsync(line).WaitAsync(Patience);

  /// <summary>Próxima línea que mandó el servidor, o <c>null</c> si cerró la conexión.</summary>
  public Task<string?> ReadLineAsync() => _reader.ReadLineAsync().WaitAsync(Patience);

  public void Dispose() => _client.Dispose();
}

/// <summary>
/// Un <see cref="NetworkServer"/> real escuchando en un puerto libre de <c>127.0.0.1</c>, con GameSession falsa. La cola de comandos es
/// la falsa (<see cref="Commands"/>) salvo que la prueba pase otra.
/// Al terminar la prueba cancela el servidor y cierra los clientes.
/// </summary>
internal sealed class NetworkServerHarness : IAsyncDisposable
{
  private readonly CancellationTokenSource _cancellation = new();
  private readonly List<TestClient> _clients = [];
  private Task _run = Task.CompletedTask;

  public NetworkServerHarness(Func<NetworkingConfig, NetworkingConfig>? configure = null, ICommandSink? commands = null)
  {
    NetworkingConfig config = ContractsTests.ValidConfig() with
    {
      ListenAddress = "127.0.0.1",
      Port = 0,
      CloseLinger = TimeSpan.FromMilliseconds(200),
    };
    if (configure is not null)
    {
      config = configure(config);
    }

    Server = new NetworkServer(config, Session, Gate, commands ?? Commands, TimeProvider.System, Loggers);
  }

  public FakeSessionEventSink Session { get; } = new();

  public FakeSessionGate Gate { get; } = new();

  public FakeCommandSink Commands { get; } = new();

  public ListLoggerFactory Loggers { get; } = new();

  public NetworkServer Server { get; }

  /// <summary>Arranca el servidor y devuelve el puerto en el que escucha.</summary>
  public async Task<int> StartAsync()
  {
    _run = Server.RunAsync(_cancellation.Token);
    return await Server.ListeningPort.WaitAsync(TimeSpan.FromSeconds(10));
  }

  public async Task<TestClient> ConnectAsync()
  {
    var client = await TestClient.ConnectAsync(await Server.ListeningPort);
    _clients.Add(client);
    return client;
  }

  /// <summary>La tarea de <see cref="NetworkServer.RunAsync"/>; termina al cancelar o al terminar el drenaje.</summary>
  public Task Running => _run;

  public Task StopAsync()
  {
    _cancellation.Cancel();
    return _run;
  }

  public async ValueTask DisposeAsync()
  {
    await _cancellation.CancelAsync();
    foreach (TestClient client in _clients)
    {
      client.Dispose();
    }

    try
    {
      await _run.WaitAsync(TimeSpan.FromSeconds(10));
    }
    catch (OperationCanceledException)
    {
    }
  }
}
