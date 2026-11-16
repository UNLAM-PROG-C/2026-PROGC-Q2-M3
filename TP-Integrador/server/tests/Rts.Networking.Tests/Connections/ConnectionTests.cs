using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Rts.Networking.Configuration;
using Rts.Networking.Connections;
using Rts.Networking.Tests.Admission;
using Rts.Networking.Tests.Outbound;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Transport;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests.Connections;

public class ConnectionTests
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  private static readonly ConnectionId Id = new(7);

  private readonly FakeSessionEventSink _session = new();
  private readonly FakeSessionGate _gate = new();
  private readonly FakeCommandSink _commands = new();
  private readonly LiveReadStream _incoming = new();
  private readonly RecordingStream _outgoing = new();

  private Connection Create(NetworkingConfig? config = null, TimeProvider? time = null) =>
      new(Id, new CompositeStream(_incoming, _outgoing), config ?? ContractsTests.ValidConfig(), _session, _gate, _commands, time ?? TimeProvider.System, NullLogger<Connection>.Instance);

  private static OutboundMessage Pong(long pingId) => new(ProtocolMessageTypes.Pong, new PongDto(pingId), DeliveryClass.Essential);

  private static string PongLine(long pingId) => "{\"protocolVersion\":1,\"type\":\"Pong\",\"payload\":{\"pingId\":" + pingId + "}}\n";

  private static string PingLine(long pingId) => "{\"protocolVersion\":1,\"type\":\"Ping\",\"payload\":{\"pingId\":" + pingId + "}}\n";

  private static string DisconnectingLine(ErrorCode reason) =>
      "{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"" + reason.Value + "\"}}\n";

  private IReadOnlyList<string> Written => [.. _outgoing.Writes.Select(w => Encoding.UTF8.GetString(w))];

  private static async Task WaitUntil(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + Patience;
    while (!condition())
    {
      Assert.True(DateTime.UtcNow < deadline, "La condición no se cumplió a tiempo.");
      await Task.Delay(5);
    }
  }

  // ───────────── Recibir ─────────────

  [Fact]
  public async Task APingFromTheClient_IsAnsweredWithPong()
  {
    var run = Create().RunAsync();

    _incoming.Feed(PingLine(5));
    await WaitUntil(() => Written.Count == 1);

    Assert.Equal(PongLine(5), Written[0]);
    _incoming.Complete();
    await run.WaitAsync(Patience);
  }

  [Fact]
  public async Task AHelloFromTheClient_ReachesTheSession()
  {
    var run = Create().RunAsync();

    _incoming.Feed("{\"protocolVersion\":1,\"type\":\"Hello\"}\n");
    await WaitUntil(() => _session.Hellos.Count == 1);

    Assert.Equal([Id], _session.Hellos);
    _incoming.Complete();
    await run.WaitAsync(Patience);
  }

  // ───────────── Cierre único (08 §3) ─────────────

  [Fact]
  public async Task WhenTheClientCloses_TheConnectionEndsAndClosedIsReportedOnce()
  {
    var run = Create().RunAsync();

    _incoming.Complete();
    await run.WaitAsync(Patience);

    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task WhenReadingFails_TheConnectionEndsAndClosedIsReportedOnce()
  {
    var run = Create().RunAsync();

    _incoming.Fail(new IOException("socket roto"));
    await run.WaitAsync(Patience);

    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task WhenWritingFails_TheConnectionEndsAndClosedIsReportedOnce()
  {
    _outgoing.WriteException = new IOException("socket roto");
    var connection = Create();
    var run = connection.RunAsync();

    connection.Send(Pong(1));
    await run.WaitAsync(Patience);

    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task WhenTheClientIsGone_WhatWasPendingIsNotDelivered()
  {
    // No hay entrega a sockets muertos: el cliente cerró, así que la salida se corta.
    var gate = new TaskCompletionSource();
    _outgoing.Gate = gate.Task;
    var connection = Create();
    var run = connection.RunAsync();
    connection.Send(Pong(1));
    await _outgoing.WriteStarted.WaitAsync(Patience);

    _incoming.Complete();
    await run.WaitAsync(Patience);

    Assert.Empty(_outgoing.Writes);
    Assert.Equal([Id], _session.Closed);
  }

  // ───────────── Close (08 §7) ─────────────

  [Fact]
  public async Task Close_WritesDisconnectingWithTheReasonAndEnds()
  {
    var connection = Create();
    var run = connection.RunAsync();

    connection.Close(NetworkErrors.ServerShuttingDown);
    await run.WaitAsync(Patience);

    Assert.Equal([DisconnectingLine(NetworkErrors.ServerShuttingDown)], Written);
    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task Close_DeliversWhatWasQueuedBeforeTheDisconnecting()
  {
    var connection = Create();
    var run = connection.RunAsync();

    connection.Send(Pong(1));
    connection.Send(Pong(2));
    connection.Close(NetworkErrors.AbuseDetected);
    await run.WaitAsync(Patience);

    Assert.Equal([PongLine(1), PongLine(2), DisconnectingLine(NetworkErrors.AbuseDetected)], Written);
  }

  [Fact]
  public async Task Close_CalledTwice_WritesOneDisconnectingAndReportsClosedOnce()
  {
    var connection = Create();
    var run = connection.RunAsync();

    connection.Close(NetworkErrors.SessionBusy);
    connection.Close(NetworkErrors.AbuseDetected);
    await run.WaitAsync(Patience);

    Assert.Equal([DisconnectingLine(NetworkErrors.SessionBusy)], Written);
    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task Send_AfterClose_IsDropped()
  {
    var connection = Create();
    var run = connection.RunAsync();
    connection.Close(NetworkErrors.SessionBusy);

    connection.Send(Pong(1));
    await run.WaitAsync(Patience);

    Assert.Equal([DisconnectingLine(NetworkErrors.SessionBusy)], Written);
  }

  [Fact]
  public async Task Close_WhenTheClientDoesNotRead_EndsAfterCloseLinger()
  {
    // El cliente no lee: la escritura nunca termina. CloseLinger corta el socket igual (08 §7).
    var gate = new TaskCompletionSource();
    _outgoing.Gate = gate.Task;
    var connection = Create(ContractsTests.ValidConfig() with { CloseLinger = TimeSpan.FromMilliseconds(200) });
    var run = connection.RunAsync();
    connection.Send(Pong(1));
    await _outgoing.WriteStarted.WaitAsync(Patience);

    connection.Close(NetworkErrors.ServerShuttingDown);
    await run.WaitAsync(Patience);

    Assert.Empty(_outgoing.Writes);
    Assert.Equal([Id], _session.Closed);
  }

  // ───────────── Cierres que decide la propia conexión ─────────────

  [Fact]
  public async Task AnOversizeLine_ClosesWithMessageTooLarge()
  {
    var config = ContractsTests.ValidConfig() with { MaxInboundMessageBytes = 64 };
    var run = Create(config).RunAsync();

    _incoming.Feed(new string('x', 65));
    await run.WaitAsync(Patience);

    Assert.Equal([DisconnectingLine(NetworkErrors.MessageTooLarge)], Written);
    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task AHelloWithAnotherVersion_GetsProtocolErrorAndThenDisconnecting()
  {
    var run = Create().RunAsync();

    _incoming.Feed("{\"protocolVersion\":2,\"type\":\"Hello\"}\n");
    await run.WaitAsync(Patience);

    Assert.Equal(2, Written.Count);
    Assert.Contains("\"type\":\"ProtocolError\"", Written[0], StringComparison.Ordinal);
    Assert.Contains("\"serverProtocolVersion\":1", Written[0], StringComparison.Ordinal);
    Assert.Equal(DisconnectingLine(NetworkErrors.UnsupportedVersion), Written[1]);
    Assert.Empty(_session.Hellos);
  }

  [Fact]
  public async Task WhenTheSessionQueueIsFull_TheHelloClosesWithSessionBusy()
  {
    _session.AcceptHello = false;
    var run = Create().RunAsync();

    _incoming.Feed("{\"protocolVersion\":1,\"type\":\"Hello\"}\n");
    await run.WaitAsync(Patience);

    Assert.Equal([DisconnectingLine(NetworkErrors.SessionBusy)], Written);
    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task WhenAnEssentialDoesNotFit_TheConnectionIsClosedForSaturation()
  {
    // D-073: un esencial que no entra desconecta al cliente. Capacidad 2: un lugar para esenciales y uno para la posición.
    var gate = new TaskCompletionSource();
    _outgoing.Gate = gate.Task;
    var connection = Create(ContractsTests.ValidConfig() with { OutboundQueueCapacity = 2 });
    var run = connection.RunAsync();
    connection.Send(Pong(1));
    await _outgoing.WriteStarted.WaitAsync(Patience); // El SendLoop ya tomó el 1 y está trabado escribiéndolo.
    connection.Send(Pong(2));

    connection.Send(Pong(3)); // No entra.
    gate.SetResult();
    await run.WaitAsync(Patience);

    Assert.Equal([PongLine(1), PongLine(2)], Written); // Lo ya encolado se entrega; el 3 no.
    Assert.Equal([Id], _session.Closed);
  }

  // ───────────── Fase (08 §3) ─────────────

  [Fact]
  public void Phase_OfANewConnection_IsAwaitingHello()
  {
    Assert.Equal(ConnectionPhase.AwaitingHello, Create().Phase);
  }

  [Fact]
  public async Task Phase_AfterAValidHello_IsHelloAccepted()
  {
    var connection = Create();
    var run = connection.RunAsync();

    _incoming.Feed("{\"protocolVersion\":1,\"type\":\"Hello\"}\n");
    await WaitUntil(() => connection.Phase == ConnectionPhase.HelloAccepted);

    _incoming.Complete();
    await run.WaitAsync(Patience);
  }

  [Fact]
  public void Phase_AfterClose_IsClosing()
  {
    var connection = Create();

    connection.Close(NetworkErrors.ServerShuttingDown);

    Assert.Equal(ConnectionPhase.Closing, connection.Phase);
  }

  [Fact]
  public async Task Phase_WhenRunEnds_IsClosed()
  {
    var connection = Create();
    var run = connection.RunAsync();

    _incoming.Complete();
    await run.WaitAsync(Patience);

    Assert.Equal(ConnectionPhase.Closed, connection.Phase);
  }

  // ───────────── Plazos (08 §3, §8) ─────────────

  private static readonly TimeSpan Instant = TimeSpan.FromMilliseconds(1);

  private static readonly TimeSpan HandshakeTimeout = ContractsTests.ValidConfig().HandshakeTimeout;

  [Fact]
  public void CheckTimeouts_BeforeTheHandshakeTimeout_KeepsWaitingForHello()
  {
    var time = new ManualTimeProvider();
    var connection = Create(time: time);

    time.Advance(HandshakeTimeout - Instant);
    connection.CheckTimeouts();

    Assert.Equal(ConnectionPhase.AwaitingHello, connection.Phase);
  }

  [Fact]
  public async Task CheckTimeouts_WithoutHelloWhenTheHandshakeTimeoutPasses_ClosesWithHandshakeTimeout()
  {
    var time = new ManualTimeProvider();
    var connection = Create(time: time);
    var run = connection.RunAsync();

    time.Advance(HandshakeTimeout);
    connection.CheckTimeouts();
    await run.WaitAsync(Patience);

    Assert.Equal([DisconnectingLine(NetworkErrors.HandshakeTimeout)], Written);
    Assert.Equal([Id], _session.Closed);
  }

  [Fact]
  public async Task CheckTimeouts_AfterHello_NoLongerAppliesTheHandshakeTimeout()
  {
    var time = new ManualTimeProvider();
    var connection = Create(time: time);
    var run = connection.RunAsync();
    _incoming.Feed("{\"protocolVersion\":1,\"type\":\"Hello\"}\n");
    await WaitUntil(() => connection.Phase == ConnectionPhase.HelloAccepted);

    time.Advance(HandshakeTimeout);
    connection.CheckTimeouts();

    Assert.Equal(ConnectionPhase.HelloAccepted, connection.Phase);
    _incoming.Complete();
    await run.WaitAsync(Patience);
  }

  private static readonly TimeSpan PingIdleInterval = ContractsTests.ValidConfig().PingIdleInterval;

  private static readonly TimeSpan PongTimeout = ContractsTests.ValidConfig().PongTimeout;

  [Fact]
  public async Task CheckTimeouts_AfterThePingIdleInterval_SendsAPingWithTheFirstId()
  {
    var time = new ManualTimeProvider();
    var connection = Create(time: time);
    var run = connection.RunAsync();

    time.Advance(PingIdleInterval);
    connection.CheckTimeouts();
    await WaitUntil(() => Written.Count == 1);

    Assert.Equal(PingLine(1), Written[0]);
    _incoming.Complete();
    await run.WaitAsync(Patience);
  }

  [Fact]
  public async Task CheckTimeouts_WhenThePingIsNotAnswered_ClosesWithHeartbeatTimeout()
  {
    var time = new ManualTimeProvider();
    var connection = Create(ContractsTests.ValidConfig() with { HandshakeTimeout = PingIdleInterval + PongTimeout + PongTimeout }, time);
    var run = connection.RunAsync();
    time.Advance(PingIdleInterval);
    connection.CheckTimeouts();

    time.Advance(PongTimeout);
    connection.CheckTimeouts();
    await run.WaitAsync(Patience);

    Assert.Equal([PingLine(1), DisconnectingLine(NetworkErrors.HeartbeatTimeout)], Written);
    Assert.Equal([Id], _session.Closed);
  }
}
