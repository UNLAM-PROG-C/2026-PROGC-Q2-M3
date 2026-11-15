using Rts.Protocol.Outbound;

namespace Rts.Networking.Outbound;

/// <summary>
/// Cola de salida acotada de una conexión (contrato 08 §5, D-078 RECOMENDADO). Varios productores y un único consumidor
/// (el <c>SendLoop</c>).
/// <list type="bullet">
/// <item>Los datos viven en una lista enlazada y todo acceso a ella pasa por un <see cref="Lock"/> que se mantiene microsegundos:
/// nunca durante una escritura al socket, que ocurre afuera, en el consumidor.</item>
/// <item>Cuando la cola está vacía, el consumidor espera sin ocupar un hilo, con un <see cref="TaskCompletionSource"/> que
/// los productores completan al encolar o al completar la cola.</item>
/// <item>Se usa una lista y no un <c>Channel</c> porque la regla de reemplazo (D-064) necesita quitar un elemento intermedio.</item>
/// </list>
/// Por ahora todos los mensajes se tratan como FIFO; el reemplazo de <see cref="DeliveryClass.Supersedable"/> se agrega aparte.
/// </summary>
internal sealed class OutboundQueue : IOutboundQueue
{
  // Misma cota que NetworkingConfig.OutboundQueueCapacity: lugar para un esencial y una posición.
  private const int MinimumCapacity = 2;

  private readonly Lock _gate = new();
  private readonly LinkedList<OutboundMessage> _items = new();
  private readonly int _capacity;

  // Lo crea el consumidor cuando encuentra la cola vacía; el productor lo completa para despertarlo. Se accede solo con _gate tomado.
  private TaskCompletionSource? _waiter;
  private bool _completed;

  public OutboundQueue(int capacity)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(capacity, MinimumCapacity);
    _capacity = capacity;
  }

  public int Count
  {
    get
    {
      lock (_gate)
      {
        return _items.Count;
      }
    }
  }

  public EnqueueOutcome TryEnqueue(OutboundMessage message)
  {
    ArgumentNullException.ThrowIfNull(message);

    TaskCompletionSource? waiter;
    lock (_gate)
    {
      if (_completed)
      {
        return EnqueueOutcome.Closed;
      }

      if (_items.Count >= _capacity)
      {
        return EnqueueOutcome.Saturated;
      }

      _items.AddLast(message);
      waiter = TakeWaiter();
    }

    // Se despierta afuera del lock para no retenerlo ni un instante más de lo necesario.
    waiter?.TrySetResult();
    return EnqueueOutcome.Enqueued;
  }

  public async ValueTask<OutboundMessage?> DequeueAsync(CancellationToken cancellationToken)
  {
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      Task wakeUp;
      lock (_gate)
      {
        if (_items.First is { } first)
        {
          _items.RemoveFirst();
          return first.Value;
        }

        if (_completed)
        {
          return null;
        }

        _waiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wakeUp = _waiter.Task;
      }

      // Afuera del lock. Si un productor encola justo ahora, el waiter ya está completo y se vuelve a intentar enseguida.
      await wakeUp.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
  }

  public void Complete()
  {
    TaskCompletionSource? waiter;
    lock (_gate)
    {
      _completed = true;
      waiter = TakeWaiter();
    }

    waiter?.TrySetResult();
  }

  // Debe llamarse con _gate tomado.
  private TaskCompletionSource? TakeWaiter()
  {
    var waiter = _waiter;
    _waiter = null;
    return waiter;
  }
}
