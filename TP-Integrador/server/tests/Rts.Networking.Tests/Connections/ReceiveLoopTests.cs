using Rts.Networking.Admission;
using Rts.Networking.Configuration;
using Rts.Networking.Connections;
using Rts.Networking.Framing;
using Rts.Networking.Parsing;
using Rts.Networking.Tests.Admission;
using Rts.Protocol;
using Rts.Protocol.Transport;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests.Connections;

public class ReceiveLoopTests
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  private static readonly NetworkingConfig Config = ContractsTests.ValidConfig();

  private const int MaxLineBytes = 100;

  private static readonly ConnectionId Connection = new(7);

  private readonly FakeSessionEventSink _session = new();
  private readonly FakeConnectionOutput _output = new();
  private readonly ScriptedStream _stream = new();

  private ReceiveLoop CreateLoop()
  {
    var admitter = new CommandAdmitter(Connection, new FakeSessionGate(), new FakeRateLimiter(), new FakeCommandSink());
    var router = new InboundRouter(Connection, 1, _session, admitter, new FakeViolationTracker(), new Heartbeat(Config.PingIdleInterval, Config.PongTimeout, TimeProvider.System), new ConnectionCounters(), _output);
    return new ReceiveLoop(new LineFrameDecoder(MaxLineBytes), new InboundMessageParser(1), router, _output, new ConnectionCounters());
  }

  private Task Run(CancellationToken cancellationToken = default) => CreateLoop().RunAsync(_stream, cancellationToken).WaitAsync(Patience);

  private static string PingLine(long pingId) => "{\"protocolVersion\":1,\"type\":\"Ping\",\"payload\":{\"pingId\":" + pingId + "}}";

  private void AssertPongs(params long[] pingIds) =>
      Assert.Equal(
          pingIds,
          _output.Sent.Select(m => Assert.IsType<PongDto>(m.Payload).PingId));

  // ───────────── Framing: qué llega del socket (08 §2, §4) ─────────────

  [Fact]
  public async Task OneCompleteLine_IsParsedAndRouted()
  {
    _stream.Add(PingLine(1) + "\n");

    await Run();

    AssertPongs(1);
  }

  [Fact]
  public async Task ALineSplitAcrossReads_IsRebuiltBeforeParsing()
  {
    string line = PingLine(2) + "\n";
    _stream.Add(line[..20]).Add(line[20..40]).Add(line[40..]);

    await Run();

    AssertPongs(2);
  }

  [Fact]
  public async Task SeveralLinesInOneRead_AreRoutedInOrder()
  {
    _stream.Add(PingLine(1) + "\n" + PingLine(2) + "\n" + PingLine(3) + "\n");

    await Run();

    AssertPongs(1, 2, 3);
  }

  [Fact]
  public async Task ALineEndingInCrLf_IsAccepted()
  {
    _stream.Add(PingLine(1) + "\r\n");

    await Run();

    AssertPongs(1);
  }

  [Fact]
  public async Task EmptyLines_AreIgnored()
  {
    _stream.Add("\n\r\n" + PingLine(1) + "\n\n");

    await Run();

    AssertPongs(1);
  }

  [Fact]
  public async Task AMessageWithoutTheLineEnd_IsNotRoutedUntilItArrives()
  {
    _stream.Add(PingLine(1));

    await Run();

    Assert.Empty(_output.Actions);
  }

  // ───────────── Qué hace con cada línea ─────────────

  [Fact]
  public async Task HelloLine_ReachesTheSession()
  {
    _stream.Add("{\"protocolVersion\":1,\"type\":\"Hello\",\"payload\":{\"clientName\":\"godot\"}}\n");

    await Run();

    Assert.Equal([Connection], _session.Hellos);
  }

  [Fact]
  public async Task AnInvalidLine_GetsAProtocolErrorAndTheLoopKeepsReading()
  {
    _stream.Add("esto no es json\n" + PingLine(1) + "\n");

    await Run();

    Assert.Equal(2, _output.Sent.Count);
    Assert.Equal(ProtocolMessageTypes.ProtocolError, _output.Sent[0].Type);
    Assert.Equal(ProtocolMessageTypes.Pong, _output.Sent[1].Type);
    Assert.Empty(_output.Closes);
  }

  // ───────────── Oversize (D-074) ─────────────

  [Fact]
  public async Task ALineOverTheMaximum_ClosesWithMessageTooLargeAndStopsReading()
  {
    string tooLong = new('x', MaxLineBytes + 1);
    _stream.Add(tooLong + "\n").Add(PingLine(1) + "\n");

    await Run();

    Assert.Equal([NetworkErrors.MessageTooLarge], _output.Closes);
    AssertPongs(); // Lo que venía después no se procesa.
  }

  [Fact]
  public async Task AnOversizeLine_IsDetectedWithoutWaitingForTheLineEnd()
  {
    // Nunca llega el \n: el cierre no puede depender de él, o un cliente podría hacer acumular bytes sin límite.
    _stream.Add(new string('x', MaxLineBytes + 1));

    await Run();

    Assert.Equal([NetworkErrors.MessageTooLarge], _output.Closes);
  }

  // ───────────── Fin de la lectura ─────────────

  [Fact]
  public async Task WhenTheClientCloses_TheLoopEndsWithoutClosingAnything()
  {
    await Run();

    Assert.Empty(_output.Actions);
  }

  [Fact]
  public async Task WhenCancelledWhileWaitingForBytes_Throws()
  {
    _stream.BlockWhenEmpty = true;
    using var cts = new CancellationTokenSource();
    var run = CreateLoop().RunAsync(_stream, cts.Token);
    await Task.Delay(100);
    Assert.False(run.IsCompleted);

    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Patience));
  }
}
