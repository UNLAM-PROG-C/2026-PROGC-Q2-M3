using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Transport;
using Rts.Shared.Commands;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking.Tests;

/// <summary>
/// La vertical slice vista desde Networking (work-plan, fase 1): dos clientes TCP reales se conectan, pasan por <c>Hello</c> y
/// <c>ClientReady</c>, uno manda un <c>Move</c> y el servidor responde a cada uno. GameSession y la cola de comandos son falsas.
/// </summary>
public class NetworkServerIntegrationTests
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  private static readonly SessionId Session = new(Guid.Parse("5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41"));

  private const string HelloLine = "{\"protocolVersion\":1,\"type\":\"Hello\"}";

  private static string ReadyLine(long initializationId) =>
      "{\"protocolVersion\":1,\"type\":\"ClientReady\",\"payload\":{\"initializationId\":" + initializationId + "}}";

  private static string MoveLine(long messageId, string extraPayloadFields = "") =>
      "{\"protocolVersion\":1,\"type\":\"Move\",\"messageId\":" + messageId + ",\"payload\":{" + extraPayloadFields +
      "\"destination\":{\"x\":10.5,\"z\":6.5},\"routeMapRevision\":0,\"routes\":[{\"unitId\":3,\"waypoints\":[{\"x\":9,\"z\":5},{\"x\":10.5,\"z\":6.5}]}]}}";

  private static string PingLine(long pingId) => "{\"protocolVersion\":1,\"type\":\"Ping\",\"payload\":{\"pingId\":" + pingId + "}}";

  private static string PongLine(long pingId) => "{\"protocolVersion\":1,\"type\":\"Pong\",\"payload\":{\"pingId\":" + pingId + "}}";

  internal static async Task WaitUntil(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + Patience;
    while (!condition())
    {
      Assert.True(DateTime.UtcNow < deadline, "La condición no se cumplió a tiempo.");
      await Task.Delay(5);
    }
  }

  /// <summary>Conecta dos clientes con su Hello y registra sus plazas en el gate, como haría GameSession.</summary>
  internal static async Task<(TestClient First, ConnectionId FirstId, TestClient Second, ConnectionId SecondId)> ConnectTwoPlayersAsync(
      NetworkServerHarness harness)
  {
    TestClient first = await harness.ConnectAsync();
    await first.SendLineAsync(HelloLine);
    await WaitUntil(() => harness.Session.Hellos.Count == 1);
    TestClient second = await harness.ConnectAsync();
    await second.SendLineAsync(HelloLine);
    await WaitUntil(() => harness.Session.Hellos.Count == 2);

    ConnectionId firstId = harness.Session.Hellos[0];
    ConnectionId secondId = harness.Session.Hellos[1];
    harness.Gate.PlayersByConnection[firstId] = PlayerId.One;
    harness.Gate.PlayersByConnection[secondId] = PlayerId.Two;
    return (first, firstId, second, secondId);
  }

  [Fact]
  public async Task TwoClients_GoThroughHelloPlayerAssignedReadyAndMatchStarted()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (first, firstId, second, secondId) = await ConnectTwoPlayersAsync(harness);

    // GameSession registra las plazas y avisa a cada cliente.
    harness.Server.Send(firstId, new PlayerAssigned(Session, PlayerId.One));
    harness.Server.Send(secondId, new PlayerAssigned(Session, PlayerId.Two));
    Assert.Contains("\"type\":\"PlayerAssigned\"", (await first.ReadLineAsync())!, StringComparison.Ordinal);
    Assert.Contains("\"playerId\":2", (await second.ReadLineAsync())!, StringComparison.Ordinal);

    // Cada cliente confirma que cargó el estado inicial.
    await first.SendLineAsync(ReadyLine(1));
    await second.SendLineAsync(ReadyLine(1));
    await WaitUntil(() => harness.Session.Readies.Count == 2);
    Assert.Equal(
        [(firstId, InitializationId.First), (secondId, InitializationId.First)],
        harness.Session.Readies.OrderBy(r => r.Connection.Value));

    // Con los dos Ready válidos, GameSession inicia la partida.
    harness.Server.Send(firstId, new MatchStarted(Session, InitializationId.First));
    harness.Server.Send(secondId, new MatchStarted(Session, InitializationId.First));
    Assert.Contains("\"type\":\"MatchStarted\"", (await first.ReadLineAsync())!, StringComparison.Ordinal);
    Assert.Contains("\"type\":\"MatchStarted\"", (await second.ReadLineAsync())!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task AMoveFromEachClient_ReachesTheCommandQueueWithThePlayerOfItsConnection()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (first, _, second, _) = await ConnectTwoPlayersAsync(harness);

    await first.SendLineAsync(MoveLine(7));
    // El segundo cliente intenta hacerse pasar por el jugador 1: el campo se ignora, el jugador sale de la conexión (08 §3).
    await second.SendLineAsync(MoveLine(8, "\"playerId\":1,"));
    await WaitUntil(() => harness.Commands.Calls.Count == 2);

    var calls = harness.Commands.Calls.OrderBy(c => c.ClientCommandId.Value).ToArray();
    Assert.Equal(PlayerId.One, calls[0].Player);
    Assert.Equal(new ClientCommandId(7), calls[0].ClientCommandId);
    Assert.IsType<MoveCommand>(calls[0].Command);
    Assert.Equal(PlayerId.Two, calls[1].Player);
    Assert.Equal(new ClientCommandId(8), calls[1].ClientCommandId);
  }

  [Fact]
  public async Task OutsideRunning_AMoveIsRejectedAndTheClientCanRetryWithTheSameMessageId()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (first, _, _, _) = await ConnectTwoPlayersAsync(harness);
    harness.Gate.AcceptsGameplay = false;

    await first.SendLineAsync(MoveLine(7));

    Assert.Equal(
        "{\"protocolVersion\":1,\"type\":\"CommandRejected\",\"payload\":{\"messageId\":7,\"error\":\"Common.MatchNotRunning\"}}",
        await first.ReadLineAsync());
    Assert.Empty(harness.Commands.Calls);

    harness.Gate.AcceptsGameplay = true;
    await first.SendLineAsync(MoveLine(7)); // D-075: se reintenta con el mismo messageId.
    await WaitUntil(() => harness.Commands.Calls.Count == 1);
  }

  [Fact]
  public async Task WhenOneClientLeaves_TheOtherKeepsWorking()
  {
    await using var harness = new NetworkServerHarness();
    await harness.StartAsync();
    var (first, firstId, second, secondId) = await ConnectTwoPlayersAsync(harness);

    first.Dispose();
    await WaitUntil(() => harness.Session.Closed.Count == 1);

    Assert.Equal([firstId], harness.Session.Closed);
    await second.SendLineAsync(PingLine(3));
    Assert.Equal(PongLine(3), await second.ReadLineAsync());
    harness.Server.Enqueue(PlayerId.Two, new OutboundMessage(ProtocolMessageTypes.Pong, new PongDto(4), DeliveryClass.Essential));
    Assert.Equal(PongLine(4), await second.ReadLineAsync());
    Assert.DoesNotContain(secondId, harness.Session.Closed);
  }
}
