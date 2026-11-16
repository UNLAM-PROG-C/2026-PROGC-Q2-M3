using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Rts.Server.Tests.EndToEnd;

/// <summary>
/// Cliente TCP de prueba que habla como Godot: JSON crudo, un mensaje por línea (08 §2). No usa los DTOs del servidor a propósito:
/// así la prueba cubre también el cable. Contesta los <c>Ping</c> solo, como haría un cliente real (08 §8).
/// </summary>
internal sealed class ProtocolTestClient : IDisposable
{
  private const int ProtocolVersion = 1;
  private const string PingType = "Ping";

  /// <summary>Cuánto se espera cada mensaje: mucho más que lo que tarda una partida local, para que falle en vez de colgarse.</summary>
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  private readonly TcpClient _client;
  private readonly StreamReader _reader;
  private readonly StreamWriter _writer;
  private readonly List<string> _received = [];

  private ProtocolTestClient(TcpClient client)
  {
    _client = client;
    NetworkStream stream = client.GetStream();
    _reader = new StreamReader(stream, new UTF8Encoding(false));
    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
  }

  /// <summary>Tipos de todos los mensajes recibidos, en orden (para entender una falla).</summary>
  public IReadOnlyList<string> Received => _received;

  public static async Task<ProtocolTestClient> ConnectAsync(int port)
  {
    var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Patience);
    return new ProtocolTestClient(client);
  }

  /// <summary>
  /// Un puerto libre de la máquina: se pide el puerto 0 al sistema, se lee cuál dio y se suelta. Otro proceso podría tomarlo justo
  /// en ese instante (carrera teórica, aceptable en pruebas).
  /// </summary>
  public static int FindFreePort()
  {
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    int port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
  }

  /// <summary>Envía un mensaje sin <c>messageId</c> (sesión y heartbeat).</summary>
  public Task SendAsync(string type, object payload) =>
      SendLineAsync(JsonSerializer.Serialize(new { protocolVersion = ProtocolVersion, type, payload }));

  /// <summary>Envía un comando de gameplay: el <c>messageId</c> es su <c>ClientCommandId</c> (08 §5).</summary>
  public Task SendCommandAsync(string type, long messageId, object payload) =>
      SendLineAsync(JsonSerializer.Serialize(new { protocolVersion = ProtocolVersion, type, messageId, payload }));

  /// <summary>Lee hasta el próximo mensaje de ese tipo y devuelve su <c>payload</c>; los demás se saltean (posiciones, eventos).</summary>
  public async Task<JsonElement> ReadUntilAsync(string type)
  {
    while (true)
    {
      (string receivedType, JsonElement payload) = await ReadMessageAsync();
      if (receivedType == type)
      {
        return payload;
      }
    }
  }

  /// <summary>Lee (y registra) lo que quede hasta que el servidor cierra la conexión; falla si no la cierra a tiempo.</summary>
  public async Task ReadUntilClosedAsync()
  {
    while (await _reader.ReadLineAsync().WaitAsync(Patience) is { } line)
    {
      _received.Add(Parse(line).Type);
    }
  }

  public void Dispose() => _client.Dispose();

  private async Task<(string Type, JsonElement Payload)> ReadMessageAsync()
  {
    string line = await _reader.ReadLineAsync().WaitAsync(Patience)
        ?? throw new InvalidOperationException($"El servidor cerró la conexión. Recibido: {string.Join(", ", _received)}.");
    (string type, JsonElement payload) = Parse(line);
    _received.Add(type);

    if (type == PingType)
    {
      await SendAsync("Pong", new { pingId = payload.GetProperty("pingId").GetInt64() });
    }

    return (type, payload);
  }

  private static (string Type, JsonElement Payload) Parse(string line)
  {
    using JsonDocument document = JsonDocument.Parse(line);
    JsonElement root = document.RootElement;
    return (root.GetProperty("type").GetString()!, root.GetProperty("payload").Clone());
  }

  private Task SendLineAsync(string line) => _writer.WriteLineAsync(line).WaitAsync(Patience);
}
