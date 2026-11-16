using System.Text;
using Rts.Networking.Connections;
using Rts.Networking.Outbound;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Session;
using Rts.Protocol.Transport;
using Rts.Shared.Match;

namespace Rts.Networking.Tests.Outbound;

public class SendLoopTests
{
  // Solo se agota si hay un bug (algo que tenía que pasar y no pasó).
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  // Tiempo que se espera para comprobar que algo NO pasa todavía.
  private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);

  private const int Capacity = 16;

  private readonly OutboundQueue _queue = new(Capacity);
  private readonly RecordingStream _stream = new();

  private static OutboundMessage Pong(long pingId) => new(ProtocolMessageTypes.Pong, new PongDto(pingId), DeliveryClass.Essential);

  private static string PongLine(long pingId, int version = 1) =>
      "{\"protocolVersion\":" + version + ",\"type\":\"Pong\",\"payload\":{\"pingId\":" + pingId + "}}\n";

  private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

  private static async Task WaitUntil(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + Patience;
    while (!condition())
    {
      Assert.True(DateTime.UtcNow < deadline, "La condición no se cumplió a tiempo.");
      await Task.Delay(5);
    }
  }

  private SendLoop CreateLoop(int version = 1) => new(_queue, _stream, version, new ConnectionCounters());

  // ───────────── Qué escribe ─────────────

  [Fact]
  public async Task RunAsync_WritesTheEnvelopeAsOneLineEndingInNewline()
  {
    var message = new OutboundMessage(
        ProtocolMessageTypes.PlayerAssigned,
        new PlayerAssignedDto("5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41", 1),
        DeliveryClass.Essential);
    _queue.TryEnqueue(message);
    _queue.Complete();

    await CreateLoop().RunAsync(CancellationToken.None).WaitAsync(Patience);

    var line = Assert.Single(_stream.Writes);
    Assert.Equal(
        """{"protocolVersion":1,"type":"PlayerAssigned","payload":{"sessionId":"5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41","playerId":1}}""" + "\n",
        Text(line));
  }

  [Fact]
  public async Task RunAsync_SerializesThePayloadByItsRealTypeWithTheProtocolConventions()
  {
    // Sin el tipo real, un IOutboundPayload saldría como {}. Nulos omitidos y enums como texto (D-072).
    var payload = new MatchEndedDto(
        "5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41", MatchResultType.Draw, null, MatchEndReason.BothBasesDestroyed, 5400);
    _queue.TryEnqueue(new OutboundMessage(ProtocolMessageTypes.MatchEnded, payload, DeliveryClass.Essential));
    _queue.Complete();

    await CreateLoop().RunAsync(CancellationToken.None).WaitAsync(Patience);

    Assert.Equal(
        """{"protocolVersion":1,"type":"MatchEnded","payload":{"sessionId":"5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41","resultType":"Draw","reason":"BothBasesDestroyed","finalTick":5400}}""" + "\n",
        Text(Assert.Single(_stream.Writes)));
  }

  [Fact]
  public async Task RunAsync_UsesTheConfiguredProtocolVersion()
  {
    _queue.TryEnqueue(Pong(7));
    _queue.Complete();

    await CreateLoop(version: 3).RunAsync(CancellationToken.None).WaitAsync(Patience);

    Assert.Equal(PongLine(7, version: 3), Text(Assert.Single(_stream.Writes)));
  }

  [Fact]
  public async Task RunAsync_WritesMessagesInQueueOrder()
  {
    for (int i = 1; i <= 5; i++)
    {
      _queue.TryEnqueue(Pong(i));
    }

    _queue.Complete();

    await CreateLoop().RunAsync(CancellationToken.None).WaitAsync(Patience);

    Assert.Equal(
        Enumerable.Range(1, 5).Select(i => PongLine(i)),
        _stream.Writes.Select(Text));
  }

  [Fact]
  public async Task RunAsync_WritesEachMessageInASingleWrite()
  {
    // Ningún mensaje intercala sus bytes con otro (08 §7): cada escritura es una línea entera.
    for (int i = 1; i <= 5; i++)
    {
      _queue.TryEnqueue(Pong(i));
    }

    _queue.Complete();

    await CreateLoop().RunAsync(CancellationToken.None).WaitAsync(Patience);

    Assert.Equal(5, _stream.Writes.Count);
    Assert.All(_stream.Writes, write =>
    {
      string line = Text(write);
      Assert.EndsWith("\n", line, StringComparison.Ordinal);
      Assert.Equal(1, line.Count(c => c == '\n'));
    });
  }

  // ───────────── Cuándo escribe y cuándo termina ─────────────

  [Fact]
  public async Task RunAsync_WhileTheQueueIsEmpty_WaitsAndThenWritesWhatArrives()
  {
    var run = CreateLoop().RunAsync(CancellationToken.None);

    await Task.Delay(ShortWait);
    Assert.False(run.IsCompleted);
    Assert.Empty(_stream.Writes);

    _queue.TryEnqueue(Pong(1));
    await WaitUntil(() => _stream.Writes.Count == 1);
    Assert.Equal(PongLine(1), Text(_stream.Writes[0]));
    Assert.False(run.IsCompleted);

    _queue.Complete();
    await run.WaitAsync(Patience);
  }

  [Fact]
  public async Task RunAsync_WhenTheQueueIsCompleted_DeliversThePendingMessagesAndThenEnds()
  {
    // Close completa la cola: lo ya encolado se entrega (por ejemplo, el Disconnecting) y recién después termina (08 §7).
    _queue.TryEnqueue(Pong(1));
    _queue.TryEnqueue(Pong(2));
    _queue.Complete();

    await CreateLoop().RunAsync(CancellationToken.None).WaitAsync(Patience);

    Assert.Equal(2, _stream.Writes.Count);
  }

  [Fact]
  public async Task RunAsync_WhenCancelledWhileWaiting_Throws()
  {
    using var cts = new CancellationTokenSource();
    var run = CreateLoop().RunAsync(cts.Token);
    await Task.Delay(ShortWait);

    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Patience));
  }

  [Fact]
  public async Task RunAsync_WhenCancelledDuringAWrite_StopsWithoutFinishingIt()
  {
    // CloseLinger vencido: el socket se corta aunque el cliente no esté leyendo (08 §7).
    var gate = new TaskCompletionSource();
    _stream.Gate = gate.Task;
    _queue.TryEnqueue(Pong(1));
    using var cts = new CancellationTokenSource();
    var run = CreateLoop().RunAsync(cts.Token);
    await _stream.WriteStarted.WaitAsync(Patience);

    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Patience));
    Assert.Empty(_stream.Writes);
  }

  // ───────────── Un cliente lento ─────────────

  [Fact]
  public async Task SlowWrite_DoesNotBlockTheProducers_AndNothingIsLostWhenItRecovers()
  {
    var gate = new TaskCompletionSource();
    _stream.Gate = gate.Task;
    _queue.TryEnqueue(Pong(1));
    var run = CreateLoop().RunAsync(CancellationToken.None);
    await _stream.WriteStarted.WaitAsync(Patience); // El SendLoop ya tomó el 1 y está trabado escribiéndolo.

    // Los productores (por ejemplo, el hilo de Simulation) siguen encolando sin esperar.
    for (int i = 2; i <= 6; i++)
    {
      Assert.Equal(EnqueueOutcome.Enqueued, _queue.TryEnqueue(Pong(i)));
    }

    Assert.Empty(_stream.Writes);

    gate.SetResult(); // El cliente se recupera.
    _queue.Complete();
    await run.WaitAsync(Patience);

    Assert.Equal(Enumerable.Range(1, 6).Select(i => PongLine(i)), _stream.Writes.Select(Text));
  }

  [Fact]
  public async Task SlowClient_DoesNotDelayAnotherConnection()
  {
    // 08 §7 y §12: un cliente lento solo llena su propia cola; no bloquea al otro cliente.
    var slowGate = new TaskCompletionSource();
    var slowStream = new RecordingStream { Gate = slowGate.Task };
    var slowQueue = new OutboundQueue(Capacity);
    var fastStream = new RecordingStream();
    var fastQueue = new OutboundQueue(Capacity);
    var slowRun = new SendLoop(slowQueue, slowStream, 1, new ConnectionCounters()).RunAsync(CancellationToken.None);
    var fastRun = new SendLoop(fastQueue, fastStream, 1, new ConnectionCounters()).RunAsync(CancellationToken.None);

    slowQueue.TryEnqueue(Pong(1));
    await slowStream.WriteStarted.WaitAsync(Patience);
    fastQueue.TryEnqueue(Pong(1));
    fastQueue.TryEnqueue(Pong(2));

    await WaitUntil(() => fastStream.Writes.Count == 2); // El rápido entrega todo mientras el lento sigue trabado.
    Assert.Empty(slowStream.Writes);

    slowGate.SetResult();
    slowQueue.Complete();
    fastQueue.Complete();
    await Task.WhenAll(slowRun, fastRun).WaitAsync(Patience);
    Assert.Single(slowStream.Writes);
  }
}
