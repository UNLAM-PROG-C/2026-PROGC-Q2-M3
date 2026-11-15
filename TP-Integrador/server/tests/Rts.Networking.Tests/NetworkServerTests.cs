using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Transport;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking.Tests;

public class NetworkServerTests
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  private const string HelloLine = "{\"protocolVersion\":1,\"type\":\"Hello\"}";

  private static string PingLine(long pingId) => "{\"protocolVersion\":1,\"type\":\"Ping\",\"payload\":{\"pingId\":" + pingId + "}}";

  private static string PongLine(long pingId) => "{\"protocolVersion\":1,\"type\":\"Pong\",\"payload\":{\"pingId\":" + pingId + "}}";

  private static OutboundMessage Pong(long pingId) => new(ProtocolMessageTypes.Pong, new PongDto(pingId), DeliveryClass.Essential);

  private static async Task WaitUntil(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + Patience;
    while (!condition())
    {
      Assert.True(DateTime.UtcNow < deadline, "La condición no se cumplió a tiempo.");
      await Task.Delay(5);
    }
  }

  /// <summary>Conecta un cliente, le manda el Hello y espera a que GameSession lo reciba. Devuelve su <see cref="ConnectionId"/>.</summary>
  private static async Task<(TestClient Client, ConnectionId Id)> ConnectWithHelloAsync(NetworkServerHarness harness)
  {
    int before = harness.Session.Hellos.Count;
    TestClient client = await harness.ConnectAsync();
    await client.SendLineAsync(HelloLine);
    await WaitUntil(() => harness.Session.Hellos.Count == before + 1);
    return (client, harness.Session.Hellos[before]);
  }

  // ───────────── Aceptar conexiones (08 §3, §9) ─────────────

  [Fact]
  public async Task AClientThatConnects_CanPingAndGetPong()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    TestClient client = await harness.ConnectAsync();

    await client.SendLineAsync(PingLine(5));

    Assert.Equal(PongLine(5), await client.ReadLineAsync());
  }

  [Fact]
  public async Task EveryConnection_GetsItsOwnId()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();

    var first = await ConnectWithHelloAsync(harness);
    var second = await ConnectWithHelloAsync(harness);

    Assert.NotEqual(first.Id, second.Id);
  }

  [Fact]
  public async Task WhenCancelled_TheServerStopsAcceptingConnections()
  {
    await using var harness = new NetworkServerHarness();
    int port = await harness.StartAsync();

    await harness.StopAsync().WaitAsync(Patience);

    await Assert.ThrowsAsync<SocketException>(() => TestClient.ConnectAsync(port));
  }

  [Fact]
  public async Task WhenTheClientDisconnects_ClosedIsReportedOnceAndTheConnectionIsForgotten()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (client, id) = await ConnectWithHelloAsync(harness);

    client.Dispose();
    await WaitUntil(() => harness.Session.Closed.Count == 1);

    Assert.Equal([id], harness.Session.Closed);
    harness.Server.Send(id, new MatchStarted(SessionId.New(), InitializationId.First)); // Ya no existe: se descarta sin fallar.
  }

  // ───────────── Send y Close (INetworkServer) ─────────────

  [Fact]
  public async Task Send_EncodesTheSessionMessageAndReachesOnlyThatConnection()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var first = await ConnectWithHelloAsync(harness);
    var second = await ConnectWithHelloAsync(harness);
    var sessionId = new SessionId(Guid.Parse("5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41"));

    harness.Server.Send(second.Id, new PlayerAssigned(sessionId, PlayerId.Two));

    Assert.Equal(
        "{\"protocolVersion\":1,\"type\":\"PlayerAssigned\",\"payload\":{\"sessionId\":\"5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41\",\"playerId\":2}}",
        await second.Client.ReadLineAsync());

    // El primero no recibió nada: lo próximo que le llega es la respuesta a su propio Ping.
    await first.Client.SendLineAsync(PingLine(1));
    Assert.Equal(PongLine(1), await first.Client.ReadLineAsync());
  }

  [Fact]
  public async Task Send_ToAnUnknownConnection_IsIgnored()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();

    harness.Server.Send(new ConnectionId(999), new MatchStarted(SessionId.New(), InitializationId.First));
    harness.Server.Close(new ConnectionId(999), NetworkErrors.ServerShuttingDown);
  }

  [Fact]
  public async Task Close_SendsDisconnectingAndThenClosesTheSocket()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (client, id) = await ConnectWithHelloAsync(harness);

    harness.Server.Close(id, NetworkErrors.ServerShuttingDown);

    Assert.Equal(
        "{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"Network.ServerShuttingDown\"}}",
        await client.ReadLineAsync());
    Assert.Null(await client.ReadLineAsync()); // El servidor cerró el socket.
    await WaitUntil(() => harness.Session.Closed.Count == 1);
    Assert.Equal([id], harness.Session.Closed);
  }

  // ───────────── Logging (D-096) ─────────────

  [Fact]
  public async Task TheServer_LogsItsPortAndEveryAcceptedConnection()
  {
    await using var harness = new NetworkServerHarness();
    int port = await harness.StartAsync();

    var (_, id) = await ConnectWithHelloAsync(harness);

    Assert.Contains(
        harness.Loggers.Entries,
        e => e.Level == LogLevel.Information && e.Message.Contains("escuchando", StringComparison.Ordinal) && e.Message.Contains(port.ToString(), StringComparison.Ordinal));
    Assert.Contains(
        harness.Loggers.Entries,
        e => e.Level == LogLevel.Information && e.Message.Contains(id.ToString(), StringComparison.Ordinal) && e.Message.Contains("aceptada", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Close_LogsTheReason()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (_, id) = await ConnectWithHelloAsync(harness);

    harness.Server.Close(id, NetworkErrors.ServerShuttingDown);

    Assert.Contains(
        harness.Loggers.Entries,
        e => e.Message.Contains(id.ToString(), StringComparison.Ordinal) && e.Message.Contains("Network.ServerShuttingDown", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData(true, "por el servidor")]
  [InlineData(false, "el cliente se fue")]
  public async Task WhenAConnectionEnds_TheLogSaysWhoEndedIt(bool closedByTheServer, string expectedCause)
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (client, id) = await ConnectWithHelloAsync(harness);

    if (closedByTheServer)
    {
      harness.Server.Close(id, NetworkErrors.ServerShuttingDown);
    }
    else
    {
      client.Dispose();
    }

    await WaitUntil(() => harness.Session.Closed.Count == 1);
    Assert.Contains(
        harness.Loggers.Entries,
        e => e.Message.Contains(id.ToString(), StringComparison.Ordinal) && e.Message.Contains("cerrada", StringComparison.Ordinal) && e.Message.Contains(expectedCause, StringComparison.Ordinal));
  }

  [Fact]
  public async Task AnUnexpectedFailureInAConnection_IsLoggedAsAnError_AndTheServerKeepsAccepting()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    harness.Session.HelloException = new InvalidOperationException("boom");
    TestClient failing = await harness.ConnectAsync();

    await failing.SendLineAsync(HelloLine);
    await WaitUntil(() => harness.Loggers.Entries.Any(e => e.Level == LogLevel.Error));

    LogEntry error = Assert.Single(harness.Loggers.Entries, e => e.Level == LogLevel.Error);
    Assert.IsType<InvalidOperationException>(error.Exception);
    Assert.Single(harness.Session.Closed); // Aun así la conexión se informó cerrada, una sola vez.

    harness.Session.HelloException = null;
    TestClient other = await harness.ConnectAsync();
    await other.SendLineAsync(PingLine(1));
    Assert.Equal(PongLine(1), await other.ReadLineAsync());
  }

  // ───────────── IClientOutbox.Enqueue (09, D-069) ─────────────

  [Fact]
  public async Task Enqueue_FindsTheConnectionOfThePlayerThroughTheGate()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (client, id) = await ConnectWithHelloAsync(harness);
    harness.Gate.ConnectionOfPlayer = id;

    harness.Server.Enqueue(PlayerId.One, Pong(9));

    Assert.Equal(PongLine(9), await client.ReadLineAsync());
  }

  [Fact]
  public async Task Enqueue_WhenThePlayerHasNoConnection_IsDropped()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (client, _) = await ConnectWithHelloAsync(harness);
    harness.Gate.ConnectionOfPlayer = null;

    harness.Server.Enqueue(PlayerId.One, Pong(9));

    await client.SendLineAsync(PingLine(1));
    Assert.Equal(PongLine(1), await client.ReadLineAsync()); // Lo primero que llega es la respuesta al Ping: el Pong 9 se descartó.
  }

  [Fact]
  public async Task Enqueue_WhenTheConnectionIsAlreadyGone_IsDropped()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (client, id) = await ConnectWithHelloAsync(harness);
    harness.Gate.ConnectionOfPlayer = id;
    client.Dispose();
    await WaitUntil(() => harness.Session.Closed.Count == 1);

    harness.Server.Enqueue(PlayerId.One, Pong(9)); // No lanza ni se entrega a un socket muerto.
  }
}
