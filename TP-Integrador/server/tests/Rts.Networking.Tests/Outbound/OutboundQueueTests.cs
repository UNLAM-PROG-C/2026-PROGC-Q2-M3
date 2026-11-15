using Rts.Networking.Outbound;
using Rts.Protocol.Outbound;

namespace Rts.Networking.Tests.Outbound;

public class OutboundQueueTests
{
  // Tiempo máximo que una prueba espera algo que "tiene que pasar". Solo se agota si hay un bug (un hilo colgado).
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  // Tiempo que se espera para comprobar que algo NO pasa todavía.
  private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);

  private sealed record TestPayload(int Producer, int Index) : IOutboundPayload;

  private static OutboundMessage Essential(int producer = 0, int index = 0) =>
      new("Test", new TestPayload(producer, index), DeliveryClass.Essential);

  // DequeueAsync no tiene tope de espera: si un mensaje esperado no llega, la prueba tiene que fallar y no quedarse colgada.
  private static async Task<OutboundMessage?> Take(OutboundQueue queue) =>
      await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(Patience);

  private static TestPayload PayloadOf(OutboundMessage? message) =>
      Assert.IsType<TestPayload>(Assert.IsType<OutboundMessage>(message).Payload);

  // ───────────── Constructor ─────────────

  [Theory]
  [InlineData(1)]
  [InlineData(0)]
  [InlineData(-1)]
  public void Constructor_CapacityBelowTwo_Throws(int capacity)
  {
    // Igual que NetworkingConfig: tiene que haber lugar para un esencial y una posición.
    Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundQueue(capacity));
  }

  [Fact]
  public void Constructor_CapacityTwo_IsValid()
  {
    var queue = new OutboundQueue(2);

    Assert.Equal(0, queue.Count);
  }

  // ───────────── Encolar y sacar ─────────────

  [Fact]
  public async Task Enqueue_ThenDequeue_ReturnsTheSameMessage()
  {
    var queue = new OutboundQueue(4);
    var message = Essential();

    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(message));
    var received = await Take(queue);

    Assert.Same(message, received);
  }

  [Fact]
  public async Task Dequeue_ReturnsMessagesInEnqueueOrder()
  {
    var queue = new OutboundQueue(4);
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Essential(index: 2));
    queue.TryEnqueue(Essential(index: 3));

    Assert.Equal(1, PayloadOf(await Take(queue)).Index);
    Assert.Equal(2, PayloadOf(await Take(queue)).Index);
    Assert.Equal(3, PayloadOf(await Take(queue)).Index);
  }

  [Fact]
  public void TryEnqueue_NullMessage_Throws()
  {
    var queue = new OutboundQueue(4);

    Assert.Throws<ArgumentNullException>(() => queue.TryEnqueue(null!));
  }

  // ───────────── Count ─────────────

  [Fact]
  public async Task Count_TracksPendingMessages()
  {
    var queue = new OutboundQueue(4);
    Assert.Equal(0, queue.Count);

    queue.TryEnqueue(Essential());
    queue.TryEnqueue(Essential());
    Assert.Equal(2, queue.Count);

    await Take(queue);
    Assert.Equal(1, queue.Count);
  }

  // ───────────── Saturación ─────────────

  [Fact]
  public void TryEnqueue_WhenEssentialsFillTheirShare_ReturnsSaturated()
  {
    // Capacidad 3 = 2 lugares para esenciales + 1 reservado para la posición.
    var queue = new OutboundQueue(3);
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Essential(index: 2));

    var outcome = queue.TryEnqueue(Essential(index: 3));

    Assert.Equal(EnqueueOutcome.Saturated, outcome);
    Assert.Equal(2, queue.Count);
  }

  [Fact]
  public async Task TryEnqueue_WhenSaturated_NeverDropsMessagesAlreadyQueued()
  {
    // D-073: para hacerle lugar a un esencial nunca se descarta otro.
    var queue = new OutboundQueue(3);
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Essential(index: 2));
    queue.TryEnqueue(Essential(index: 3));

    Assert.Equal(1, PayloadOf(await Take(queue)).Index);
    Assert.Equal(2, PayloadOf(await Take(queue)).Index);
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public async Task TryEnqueue_AfterTheConsumerFreesASlot_AcceptsAgain()
  {
    var queue = new OutboundQueue(3);
    queue.TryEnqueue(Essential());
    queue.TryEnqueue(Essential());
    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Essential()));

    await Take(queue);

    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Essential()));
  }

  // ───────────── DequeueAsync espera ─────────────

  [Fact]
  public async Task DequeueAsync_OnEmptyQueue_WaitsWithoutBlocking()
  {
    var queue = new OutboundQueue(4);

    var pending = queue.DequeueAsync(CancellationToken.None).AsTask();

    // Volvió enseguida con una tarea sin terminar: esperar no ocupa el hilo.
    await Task.Delay(ShortWait);
    Assert.False(pending.IsCompleted);

    var message = Essential();
    queue.TryEnqueue(message);
    Assert.Same(message, await pending.WaitAsync(Patience));
  }

  [Fact]
  public async Task DequeueAsync_WakesUpOnEveryEnqueue()
  {
    var queue = new OutboundQueue(4);

    for (int i = 1; i <= 3; i++)
    {
      var pending = queue.DequeueAsync(CancellationToken.None).AsTask();
      await Task.Delay(10);
      queue.TryEnqueue(Essential(index: i));

      Assert.Equal(i, PayloadOf(await pending.WaitAsync(Patience)).Index);
    }
  }

  [Fact]
  public async Task Enqueue_DoesNotRunTheConsumerOnTheProducerThread()
  {
    // El productor puede ser el hilo de Simulation: no puede terminar ejecutando el código del SendLoop (08 §5).
    var queue = new OutboundQueue(4);
    int consumerThread = -1;
    var consumer = Task.Run(async () =>
    {
      await queue.DequeueAsync(CancellationToken.None);
      consumerThread = Environment.CurrentManagedThreadId; // Se ejecuta después de despertar.
    });
    await Task.Delay(ShortWait); // Que el consumidor ya esté esperando.

    int producerThread = -1;
    var producer = new Thread(() =>
    {
      producerThread = Environment.CurrentManagedThreadId;
      queue.TryEnqueue(Essential());
    });
    producer.Start();
    producer.Join();
    await consumer.WaitAsync(Patience);

    Assert.NotEqual(-1, consumerThread);
    Assert.NotEqual(producerThread, consumerThread);
  }

  // ───────────── Cancelación ─────────────

  [Fact]
  public async Task DequeueAsync_WhenCancelledWhileWaiting_Throws()
  {
    var queue = new OutboundQueue(4);
    using var cts = new CancellationTokenSource();
    var pending = queue.DequeueAsync(cts.Token).AsTask();
    await Task.Delay(ShortWait);

    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Patience));
  }

  [Fact]
  public async Task DequeueAsync_WithAlreadyCancelledToken_Throws()
  {
    var queue = new OutboundQueue(4);
    queue.TryEnqueue(Essential());
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.DequeueAsync(cts.Token).AsTask());
  }

  [Fact]
  public async Task DequeueAsync_AfterACancelledWait_StillWorks()
  {
    // Una espera cancelada no puede dejar la cola rota: después se puede volver a esperar y recibir normalmente.
    var queue = new OutboundQueue(4);
    using var cts = new CancellationTokenSource();
    var cancelled = queue.DequeueAsync(cts.Token).AsTask();
    await Task.Delay(ShortWait);
    await cts.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Patience));

    var retry = queue.DequeueAsync(CancellationToken.None).AsTask();
    var message = Essential();
    queue.TryEnqueue(message);

    Assert.Same(message, await retry.WaitAsync(Patience));
  }

  // ───────────── Complete ─────────────

  [Fact]
  public void TryEnqueue_AfterComplete_ReturnsClosed()
  {
    var queue = new OutboundQueue(4);
    queue.Complete();

    Assert.Equal(EnqueueOutcome.Closed, queue.TryEnqueue(Essential()));
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public async Task Complete_DeliversPendingMessagesAndThenReturnsNull()
  {
    var queue = new OutboundQueue(4);
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Essential(index: 2));
    queue.Complete();

    Assert.Equal(1, PayloadOf(await Take(queue)).Index);
    Assert.Equal(2, PayloadOf(await Take(queue)).Index);
    Assert.Null(await Take(queue));
    Assert.Null(await Take(queue));
  }

  [Fact]
  public async Task Complete_OnEmptyQueue_DequeueReturnsNullRightAway()
  {
    var queue = new OutboundQueue(4);
    queue.Complete();

    Assert.Null(await Take(queue));
  }

  [Fact]
  public async Task Complete_WakesUpAWaitingConsumer()
  {
    var queue = new OutboundQueue(4);
    var pending = queue.DequeueAsync(CancellationToken.None).AsTask();
    await Task.Delay(ShortWait);
    Assert.False(pending.IsCompleted);

    queue.Complete();

    Assert.Null(await pending.WaitAsync(Patience));
  }

  [Fact]
  public void Complete_CalledTwice_IsHarmless()
  {
    var queue = new OutboundQueue(4);

    queue.Complete();
    queue.Complete();

    Assert.Equal(EnqueueOutcome.Closed, queue.TryEnqueue(Essential()));
  }

  // ───────────── Concurrencia ─────────────

  [Fact]
  public async Task ManyProducers_AreAllDeliveredInTheOrderEachOneEnqueued()
  {
    const int producers = 8;
    const int perProducer = 2_000;
    // Capacidad de sobra (+1 por el lugar reservado): acá se prueba que no se pierda ni se reordene nada, no la saturación.
    var queue = new OutboundQueue((producers * perProducer) + 1);
    using var start = new ManualResetEventSlim(false);

    var producerTasks = Enumerable.Range(0, producers).Select(producer => Task.Factory.StartNew(
        () =>
        {
          start.Wait();
          for (int i = 0; i < perProducer; i++)
          {
            Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Essential(producer, i)));
          }
        },
        TaskCreationOptions.LongRunning)).ToArray();

    start.Set(); // Todos los productores arrancan juntos.
    await Task.WhenAll(producerTasks).WaitAsync(Patience);
    queue.Complete();

    var lastIndexSeen = new int[producers];
    Array.Fill(lastIndexSeen, -1);
    int total = 0;
    while (await queue.DequeueAsync(CancellationToken.None) is { } message)
    {
      var payload = PayloadOf(message);
      Assert.Equal(lastIndexSeen[payload.Producer] + 1, payload.Index); // Sin huecos y en orden.
      lastIndexSeen[payload.Producer] = payload.Index;
      total++;
    }

    Assert.Equal(producers * perProducer, total);
  }

  [Fact]
  public async Task ProducersAndConsumerRunningTogether_DeliverEverythingExactlyOnce()
  {
    const int producers = 4;
    const int perProducer = 5_000;
    var queue = new OutboundQueue((producers * perProducer) + 1);
    var seen = new bool[producers, perProducer];

    var consumer = Task.Run(async () =>
    {
      int received = 0;
      while (received < producers * perProducer)
      {
        var message = await queue.DequeueAsync(CancellationToken.None);
        var payload = PayloadOf(message);
        Assert.False(seen[payload.Producer, payload.Index], "Mensaje entregado dos veces.");
        seen[payload.Producer, payload.Index] = true;
        received++;
      }
    });

    var producerTasks = Enumerable.Range(0, producers).Select(producer => Task.Run(() =>
    {
      for (int i = 0; i < perProducer; i++)
      {
        queue.TryEnqueue(Essential(producer, i));
      }
    }));

    await Task.WhenAll(producerTasks).WaitAsync(Patience);
    await consumer.WaitAsync(Patience);

    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public async Task SlowConsumer_NeverBlocksProducers_AndCapacityIsNeverExceeded()
  {
    // Nadie saca nada (el consumidor está "colgado"): los productores igual terminan, y la cola no pasa de su capacidad.
    // Con capacidad 4, los esenciales usan como máximo 3 (el cuarto lugar es de la posición).
    const int capacity = 4;
    const int essentialShare = capacity - 1;
    const int producers = 4;
    const int perProducer = 1_000;
    var queue = new OutboundQueue(capacity);
    int enqueued = 0;
    int saturated = 0;

    var producerTasks = Enumerable.Range(0, producers).Select(producer => Task.Run(() =>
    {
      for (int i = 0; i < perProducer; i++)
      {
        switch (queue.TryEnqueue(Essential(producer, i)))
        {
          case EnqueueOutcome.Enqueued:
            Interlocked.Increment(ref enqueued);
            break;
          case EnqueueOutcome.Saturated:
            Interlocked.Increment(ref saturated);
            break;
          default:
            throw new InvalidOperationException("Resultado inesperado.");
        }
      }
    }));

    await Task.WhenAll(producerTasks).WaitAsync(Patience);

    Assert.Equal(essentialShare, enqueued);
    Assert.Equal((producers * perProducer) - essentialShare, saturated);
    Assert.Equal(essentialShare, queue.Count);
  }
}
