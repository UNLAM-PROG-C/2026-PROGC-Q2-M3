using Rts.Networking.Outbound;
using Rts.Protocol.Outbound;

namespace Rts.Networking.Tests.Outbound;

/// <summary>Reemplazo de mensajes <see cref="DeliveryClass.Supersedable"/> (D-064) y lugar reservado para la posición.</summary>
public class OutboundQueueSupersedeTests
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);

  private const string PositionType = "PositionUpdate";

  private sealed record TestPayload(int Producer, int Index) : IOutboundPayload;

  private static OutboundMessage Essential(int producer = 0, int index = 0) =>
      new("StateEvents", new TestPayload(producer, index), DeliveryClass.Essential);

  private static OutboundMessage Position(int index, string type = PositionType) =>
      new(type, new TestPayload(0, index), DeliveryClass.Supersedable);

  // Con tope de espera: si el mensaje esperado no llega, la prueba falla en vez de quedarse colgada.
  private static async Task<int> NextIndex(OutboundQueue queue) =>
      Assert.IsType<TestPayload>(
          Assert.IsType<OutboundMessage>(await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(Patience)).Payload).Index;

  // ───────────── Reemplazo ─────────────

  [Fact]
  public void TryEnqueue_FirstSupersedable_IsJustEnqueued()
  {
    var queue = new OutboundQueue(4);

    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Position(1)));
    Assert.Equal(1, queue.Count);
  }

  [Fact]
  public async Task TryEnqueue_SecondSupersedable_ReplacesThePendingOne()
  {
    var queue = new OutboundQueue(4);
    queue.TryEnqueue(Position(1));

    var outcome = queue.TryEnqueue(Position(2));

    Assert.Equal(EnqueueOutcome.Superseded, outcome);
    Assert.Equal(1, queue.Count);
    Assert.Equal(2, await NextIndex(queue));
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public async Task TryEnqueue_Replacement_GoesToTheEndOfTheQueue()
  {
    var queue = new OutboundQueue(6);
    queue.TryEnqueue(Position(1));
    queue.TryEnqueue(Essential(index: 10));

    queue.TryEnqueue(Position(2));

    // El nuevo no se adelanta al esencial: va detrás (09 §7).
    Assert.Equal(10, await NextIndex(queue));
    Assert.Equal(2, await NextIndex(queue));
  }

  [Fact]
  public async Task TryEnqueue_Replacement_NeverCrossesEssentialMessages()
  {
    var queue = new OutboundQueue(8);
    queue.TryEnqueue(Position(1));
    queue.TryEnqueue(Essential(index: 10));
    queue.TryEnqueue(Essential(index: 11));
    queue.TryEnqueue(Essential(index: 12));

    queue.TryEnqueue(Position(2));
    queue.TryEnqueue(Essential(index: 13));
    queue.TryEnqueue(Position(3));

    // Los esenciales conservan su orden; solo desaparecen las posiciones viejas.
    Assert.Equal(10, await NextIndex(queue));
    Assert.Equal(11, await NextIndex(queue));
    Assert.Equal(12, await NextIndex(queue));
    Assert.Equal(13, await NextIndex(queue));
    Assert.Equal(3, await NextIndex(queue));
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public async Task TryEnqueue_WhenTheConsumerAlreadyTookThePending_DoesNotTouchIt()
  {
    // 09 §7: un mensaje que el SendLoop ya tomó (ya empezó a enviarse) no se reemplaza.
    var queue = new OutboundQueue(4);
    var first = Position(1);
    queue.TryEnqueue(first);
    Assert.Same(first, await queue.DequeueAsync(CancellationToken.None));

    var outcome = queue.TryEnqueue(Position(2));

    Assert.Equal(EnqueueOutcome.Enqueued, outcome);
    Assert.Equal(2, await NextIndex(queue));
  }

  [Fact]
  public async Task TryEnqueue_ReplacementAfterAnEssentialWasTaken_StillReplacesThePosition()
  {
    var queue = new OutboundQueue(4);
    queue.TryEnqueue(Essential(index: 10));
    queue.TryEnqueue(Position(1));
    Assert.Equal(10, await NextIndex(queue)); // El consumidor toma el esencial del frente.

    Assert.Equal(EnqueueOutcome.Superseded, queue.TryEnqueue(Position(2)));

    Assert.Equal(2, await NextIndex(queue));
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public async Task TryEnqueue_SupersedableOfAnotherType_DoesNotReplaceIt()
  {
    // "Del mismo tipo" (09 §7): solo se reemplaza un pendiente con el mismo Type.
    var queue = new OutboundQueue(4);
    queue.TryEnqueue(Position(1, type: "TypeA"));

    var outcome = queue.TryEnqueue(Position(2, type: "TypeB"));

    Assert.Equal(EnqueueOutcome.Enqueued, outcome);
    Assert.Equal(2, queue.Count);
    Assert.Equal(1, await NextIndex(queue));
    Assert.Equal(2, await NextIndex(queue));
  }

  [Fact]
  public async Task DequeueAsync_WakesUpForASupersedable()
  {
    var queue = new OutboundQueue(4);
    var pending = queue.DequeueAsync(CancellationToken.None).AsTask();
    await Task.Delay(ShortWait);

    var message = Position(1);
    queue.TryEnqueue(message);

    Assert.Same(message, await pending.WaitAsync(Patience));
  }

  [Fact]
  public void TryEnqueue_SupersedableAfterComplete_ReturnsClosed()
  {
    var queue = new OutboundQueue(4);
    queue.TryEnqueue(Position(1));
    queue.Complete();

    Assert.Equal(EnqueueOutcome.Closed, queue.TryEnqueue(Position(2)));
    Assert.Equal(1, queue.Count);
  }

  // ───────────── Lugar reservado para la posición (provisorio, decisions.md) ─────────────

  [Fact]
  public void TryEnqueue_Essentials_UseAtMostCapacityMinusOne()
  {
    var queue = new OutboundQueue(4);

    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Essential(index: 1)));
    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Essential(index: 2)));
    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Essential(index: 3)));
    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Essential(index: 4)));
    Assert.Equal(3, queue.Count);
  }

  [Fact]
  public void TryEnqueue_Position_AlwaysFitsEvenWhenEssentialsFilledTheirShare()
  {
    var queue = new OutboundQueue(3);
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Essential(index: 2));
    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Essential(index: 3)));

    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Position(1)));
    Assert.Equal(EnqueueOutcome.Superseded, queue.TryEnqueue(Position(2)));
    Assert.Equal(EnqueueOutcome.Superseded, queue.TryEnqueue(Position(3)));
    Assert.Equal(3, queue.Count);
  }

  [Fact]
  public void TryEnqueue_Essential_WhenAPositionIsPendingAndEssentialsAreAtTheirShare_IsSaturated()
  {
    var queue = new OutboundQueue(3);
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Position(1));
    queue.TryEnqueue(Essential(index: 2));
    Assert.Equal(3, queue.Count);

    // La cola está llena de verdad: el esencial no entra y no se descarta la posición para hacerle lugar.
    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Essential(index: 3)));
    Assert.Equal(3, queue.Count);
  }

  [Fact]
  public async Task TryEnqueue_Essential_FitsAgainAfterTheConsumerTakesOne()
  {
    var queue = new OutboundQueue(3);
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Essential(index: 2));
    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Essential(index: 3)));

    Assert.Equal(1, await NextIndex(queue));

    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Essential(index: 3)));
  }

  [Fact]
  public async Task TryEnqueue_Essential_TakingAPositionDoesNotFreeAnEssentialSlot()
  {
    var queue = new OutboundQueue(3);
    queue.TryEnqueue(Position(1));
    queue.TryEnqueue(Essential(index: 1));
    queue.TryEnqueue(Essential(index: 2));
    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Essential(index: 3)));

    Assert.Equal(1, await NextIndex(queue)); // Sale la posición.

    // Ahora hay un lugar libre en la cola, pero los esenciales siguen en su tope (capacidad − 1): ese lugar es de la posición.
    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Essential(index: 3)));
    Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Position(2)));
  }

  [Fact]
  public void TryEnqueue_SupersedableOfAnotherType_WhenThereIsNoRoom_IsSaturatedInsteadOfExceedingTheCapacity()
  {
    // Solo PositionUpdate es reemplazable, así que el lugar reservado es uno. Un segundo tipo sin lugar no puede romper la cota.
    var queue = new OutboundQueue(2);
    queue.TryEnqueue(Position(1, type: "TypeA"));
    queue.TryEnqueue(Essential(index: 1));

    Assert.Equal(EnqueueOutcome.Saturated, queue.TryEnqueue(Position(2, type: "TypeB")));
    Assert.Equal(2, queue.Count);
  }

  // ───────────── Concurrencia ─────────────

  [Fact]
  public async Task PositionsAndEssentialsRunningTogether_LoseNoEssentialAndNeverGoBackInTime()
  {
    const int essentialProducers = 3;
    const int perProducer = 2_000;
    const int positions = 10_000;
    // Capacidad de sobra para los esenciales: acá se prueba el reemplazo, no la saturación.
    var queue = new OutboundQueue((essentialProducers * perProducer) + 2);
    using var start = new ManualResetEventSlim(false);

    var essentialTasks = Enumerable.Range(1, essentialProducers).Select(producer => Task.Factory.StartNew(
        () =>
        {
          start.Wait();
          for (int i = 0; i < perProducer; i++)
          {
            Assert.Equal(EnqueueOutcome.Enqueued, queue.TryEnqueue(Essential(producer, i)));
          }
        },
        TaskCreationOptions.LongRunning)).ToArray();

    // Como el hilo de Simulation: un único productor de posiciones, con índices crecientes.
    var positionTask = Task.Factory.StartNew(
        () =>
        {
          start.Wait();
          for (int i = 1; i <= positions; i++)
          {
            var outcome = queue.TryEnqueue(Position(i));
            Assert.True(outcome is EnqueueOutcome.Enqueued or EnqueueOutcome.Superseded);
          }
        },
        TaskCreationOptions.LongRunning);

    var lastEssential = new int[essentialProducers + 1];
    Array.Fill(lastEssential, -1);
    int essentialsReceived = 0;
    int lastPosition = 0;
    var producersDone = Task.WhenAll(essentialTasks.Append(positionTask)).ContinueWith(_ => queue.Complete(), TaskScheduler.Default);

    start.Set();

    // El consumidor corre a la vez que los productores.
    var consumer = Task.Run(async () =>
    {
      while (await queue.DequeueAsync(CancellationToken.None) is { } message)
      {
        var payload = Assert.IsType<TestPayload>(message.Payload);
        if (message.Delivery == DeliveryClass.Essential)
        {
          Assert.Equal(lastEssential[payload.Producer] + 1, payload.Index);
          lastEssential[payload.Producer] = payload.Index;
          essentialsReceived++;
        }
        else
        {
          Assert.True(payload.Index > lastPosition, "Una posición vieja llegó después de una nueva.");
          lastPosition = payload.Index;
        }
      }
    });

    await Task.WhenAll(essentialTasks.Append(positionTask).Append(producersDone).Append(consumer)).WaitAsync(Patience);

    Assert.Equal(essentialProducers * perProducer, essentialsReceived);
    Assert.Equal(positions, lastPosition); // La última posición nunca se pierde: solo la reemplaza una más nueva.
  }

  [Fact]
  public async Task PositionsUnderPressure_NeverSaturate_AndTheQueueNeverExceedsItsCapacity()
  {
    // Cola chica y sin consumidor: los esenciales la llenan hasta su tope y las posiciones igual entran.
    const int capacity = 4;
    var queue = new OutboundQueue(capacity);
    using var start = new ManualResetEventSlim(false);

    var essentialTasks = Enumerable.Range(1, 3).Select(producer => Task.Factory.StartNew(
        () =>
        {
          start.Wait();
          for (int i = 0; i < 2_000; i++)
          {
            var outcome = queue.TryEnqueue(Essential(producer, i));
            Assert.True(outcome is EnqueueOutcome.Enqueued or EnqueueOutcome.Saturated);
          }
        },
        TaskCreationOptions.LongRunning)).ToArray();

    var positionTask = Task.Factory.StartNew(
        () =>
        {
          start.Wait();
          for (int i = 1; i <= 10_000; i++)
          {
            var outcome = queue.TryEnqueue(Position(i));
            Assert.True(outcome is EnqueueOutcome.Enqueued or EnqueueOutcome.Superseded, $"La posición {i} dio {outcome}.");
            Assert.True(queue.Count <= capacity);
          }
        },
        TaskCreationOptions.LongRunning);

    start.Set();
    await Task.WhenAll(essentialTasks.Append(positionTask)).WaitAsync(Patience);

    Assert.Equal(capacity, queue.Count); // 3 esenciales + la última posición.
  }
}
