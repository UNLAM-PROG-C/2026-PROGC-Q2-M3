using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Transport;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Match;
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

  // ───────────── Abuso (08 §6, D-074) ─────────────

  [Fact]
  public async Task AClientThatKeepsSendingInvalidLines_IsClosedWithAbuseDetected()
  {
    const int maxViolations = 2;
    await using var harness = new NetworkServerHarness(config => config with { MaxViolations = maxViolations });
    await harness.StartAsync();
    TestClient client = await harness.ConnectAsync();

    for (int i = 0; i <= maxViolations; i++)
    {
      await client.SendLineAsync("esto no es JSON");
    }

    // Cada falta recibe su ProtocolError; la que supera el límite, además, el cierre.
    for (int i = 0; i <= maxViolations; i++)
    {
      Assert.Contains("Network.MalformedJson", await client.ReadLineAsync());
    }

    Assert.Equal(
        "{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"Network.AbuseDetected\"}}",
        await client.ReadLineAsync());
    Assert.Null(await client.ReadLineAsync());
    await WaitUntil(() => harness.Session.Closed.Count == 1);
  }

  [Fact]
  public async Task AClientWithinTheViolationLimit_KeepsItsConnection()
  {
    const int maxViolations = 2;
    await using var harness = new NetworkServerHarness(config => config with { MaxViolations = maxViolations });
    await harness.StartAsync();
    TestClient client = await harness.ConnectAsync();

    for (int i = 0; i < maxViolations; i++)
    {
      await client.SendLineAsync("esto no es JSON");
      Assert.Contains("Network.MalformedJson", await client.ReadLineAsync());
    }

    await client.SendLineAsync(PingLine(3));
    Assert.Equal(PongLine(3), await client.ReadLineAsync());
  }

  // ───────────── Conexiones pendientes (08 §3) ─────────────

  /// <summary>Conecta un cliente sin Hello y espera a que el servidor lo haya aceptado (responde un Ping).</summary>
  private static async Task<TestClient> ConnectWithoutHelloAsync(NetworkServerHarness harness)
  {
    TestClient client = await harness.ConnectAsync();
    await client.SendLineAsync(PingLine(1));
    Assert.Equal(PongLine(1), await client.ReadLineAsync());
    return client;
  }

  [Fact]
  public async Task WithMaxPendingConnectionsWaitingForHello_TheNextOneIsClosedWithTooManyConnections()
  {
    const int maxPending = 2;
    await using var harness = new NetworkServerHarness(config => config with { MaxPendingConnections = maxPending });
    await harness.StartAsync();
    for (int i = 0; i < maxPending; i++)
    {
      await ConnectWithoutHelloAsync(harness);
    }

    TestClient extra = await harness.ConnectAsync();

    Assert.Equal(
        "{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"Network.TooManyConnections\"}}",
        await extra.ReadLineAsync());
    Assert.Null(await extra.ReadLineAsync());
    await WaitUntil(() => harness.Session.Closed.Count == 1);
  }

  [Fact]
  public async Task AConnectionThatSentItsHello_NoLongerCountsAsPending()
  {
    await using var harness = new NetworkServerHarness(config => config with { MaxPendingConnections = 1 });
    await harness.StartAsync();
    await ConnectWithHelloAsync(harness);

    // La primera ya mandó su Hello: hay lugar para otra pendiente.
    await ConnectWithoutHelloAsync(harness);

    Assert.Empty(harness.Session.Closed);
  }

  // ───────────── Monitor: timeout de handshake (08 §3, §8) ─────────────

  private static readonly TimeSpan ShortHandshake = TimeSpan.FromMilliseconds(200);

  [Fact]
  public async Task AClientThatNeverSendsHello_IsClosedWithHandshakeTimeout()
  {
    await using var harness = new NetworkServerHarness(config => config with { HandshakeTimeout = ShortHandshake });
    await harness.StartAsync();

    TestClient client = await harness.ConnectAsync();

    Assert.Equal(
        "{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"Network.HandshakeTimeout\"}}",
        await client.ReadLineAsync());
    Assert.Null(await client.ReadLineAsync());
    await WaitUntil(() => harness.Session.Closed.Count == 1);
  }

  [Fact]
  public async Task AClientThatSentHello_OutlivesTheHandshakeTimeout()
  {
    await using var harness = new NetworkServerHarness(config => config with { HandshakeTimeout = ShortHandshake });
    await harness.StartAsync();
    var (client, _) = await ConnectWithHelloAsync(harness);

    await Task.Delay(ShortHandshake * 2);

    await client.SendLineAsync(PingLine(4));
    Assert.Equal(PongLine(4), await client.ReadLineAsync());
  }

  // ───────────── Monitor: heartbeat (08 §8) ─────────────

  private static readonly TimeSpan ShortHeartbeat = TimeSpan.FromMilliseconds(200);

  private static NetworkServerHarness HarnessWithShortHeartbeat() =>
      new(config => config with { PingIdleInterval = ShortHeartbeat, PongTimeout = ShortHeartbeat });

  [Fact]
  public async Task AnIdleClient_ReceivesAPing()
  {
    await using var harness = HarnessWithShortHeartbeat();
    await harness.StartAsync();
    var (client, _) = await ConnectWithHelloAsync(harness);

    Assert.Equal(PingLine(1), await client.ReadLineAsync());
  }

  [Fact]
  public async Task AClientThatDoesNotAnswerThePing_IsClosedWithHeartbeatTimeoutOnce()
  {
    await using var harness = HarnessWithShortHeartbeat();
    await harness.StartAsync();
    var (client, id) = await ConnectWithHelloAsync(harness);

    Assert.Equal(PingLine(1), await client.ReadLineAsync());
    Assert.Equal(
        "{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"Network.HeartbeatTimeout\"}}",
        await client.ReadLineAsync());
    Assert.Null(await client.ReadLineAsync());
    await WaitUntil(() => harness.Session.Closed.Count == 1);
    Assert.Equal([id], harness.Session.Closed);
  }

  [Fact]
  public async Task AClientThatAnswersThePing_KeepsItsConnection()
  {
    await using var harness = HarnessWithShortHeartbeat();
    await harness.StartAsync();
    var (client, _) = await ConnectWithHelloAsync(harness);

    Assert.Equal(PingLine(1), await client.ReadLineAsync());
    await client.SendLineAsync(PongLine(1));

    // Sigue viva: después de otro intervalo sin actividad llega el Ping siguiente, no el cierre.
    Assert.Equal(PingLine(2), await client.ReadLineAsync());
  }

  // ───────────── Drenaje final (08 §7, 07 §9) ─────────────

  private const string ShuttingDownLine =
      "{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"Network.ServerShuttingDown\"}}";

  private static readonly MatchEnded EndOfMatch =
      new(SessionId.New(), new MatchResult(MatchResultType.TechnicalEnd, null, MatchEndReason.ServerShutdown, null));

  [Fact]
  public async Task Drain_SendsShuttingDownBehindMatchEndedToEveryConnectionAndClosesThem()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var first = await ConnectWithHelloAsync(harness);
    var second = await ConnectWithHelloAsync(harness);
    harness.Server.Send(first.Id, EndOfMatch);
    harness.Server.Send(second.Id, EndOfMatch);

    await harness.Server.DrainAndCloseAllAsync().WaitAsync(Patience);

    foreach (TestClient client in new[] { first.Client, second.Client })
    {
      Assert.Contains("\"type\":\"MatchEnded\"", await client.ReadLineAsync());
      Assert.Equal(ShuttingDownLine, await client.ReadLineAsync());
      Assert.Null(await client.ReadLineAsync());
    }

    Assert.Equal(2, harness.Session.Closed.Count);
  }

  [Fact]
  public async Task Drain_StopsAcceptingConnections()
  {
    await using var harness = new NetworkServerHarness();
    int port = await harness.StartAsync();

    await harness.Server.DrainAndCloseAllAsync().WaitAsync(Patience);

    await harness.Running.WaitAsync(Patience);
    await Assert.ThrowsAsync<SocketException>(() => TestClient.ConnectAsync(port));
  }

  [Fact]
  public async Task Drain_WithoutConnections_Finishes()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();

    await harness.Server.DrainAndCloseAllAsync().WaitAsync(Patience);
  }

  private sealed record BulkPayload(string Data) : IOutboundPayload;

  [Fact]
  public async Task Drain_WithAClientThatStoppedReading_FinishesWhenTheDrainTimeoutPasses()
  {
    // Un cliente que no lee llena los buffers del socket: el SendLoop queda trabado escribiendo. CloseLinger largo, para que lo
    // único que pueda destrabarlo sea el DrainTimeout.
    TimeSpan drainTimeout = TimeSpan.FromMilliseconds(300);
    TimeSpan finishBudget = TimeSpan.FromSeconds(5);
    const int bulkMessages = 200;
    const int bulkMessageChars = 64 * 1024;
    await using var harness = new NetworkServerHarness(config => config with
    {
      DrainTimeout = drainTimeout,
      CloseLinger = TimeSpan.FromMinutes(1),
      OutboundQueueCapacity = bulkMessages * 2,
    });
    await harness.StartAsync();
    var (_, id) = await ConnectWithHelloAsync(harness);
    harness.Gate.ConnectionOfPlayer = id;
    var bulk = new OutboundMessage("Bulk", new BulkPayload(new string('x', bulkMessageChars)), DeliveryClass.Essential);
    for (int i = 0; i < bulkMessages; i++)
    {
      harness.Server.Enqueue(PlayerId.One, bulk);
    }

    await harness.Server.DrainAndCloseAllAsync().WaitAsync(finishBudget);

    Assert.Equal([id], harness.Session.Closed);
  }
}
