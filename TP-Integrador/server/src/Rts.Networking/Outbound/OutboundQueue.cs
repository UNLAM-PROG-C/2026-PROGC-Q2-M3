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
/// <para>
/// <b>Reemplazo (D-064):</b> un <see cref="DeliveryClass.Supersedable"/> quita al pendiente del mismo <c>Type</c> que el consumidor
/// todavía no tomó, y el nuevo va al final. Un mensaje ya tomado no se toca.
/// </para>
/// <para>
/// <b>Lugar reservado (PROVISORIO, ver «Comportamientos provisorios» en <c>docs/decisions.md</c>):</b> el contrato dice que las
/// posiciones nunca saturan la cola, pero no cubre una cola llena de esenciales sin posición pendiente. Para que una posición
/// siempre entre, los esenciales ocupan como máximo <c>capacidad − 1</c>. Esa reserva alcanza para un solo tipo reemplazable
/// (hoy solo <c>PositionUpdate</c>).
/// </para>
/// </summary>
internal sealed class OutboundQueue : IOutboundQueue
{
  // Misma cota que NetworkingConfig.OutboundQueueCapacity: lugar para un esencial y una posición.
  private const int MinimumCapacity = 2;

  // Lugares que los esenciales no pueden usar: el de la posición.
  private const int ReservedForSupersedable = 1;

  private readonly Lock _gate = new();
  private readonly LinkedList<OutboundMessage> _items = new();
  private readonly int _capacity;

  // Reemplazables pendientes por tipo, con su nodo en _items para quitarlo en O(1). Se accede solo con _gate tomado.
  private readonly Dictionary<string, LinkedListNode<OutboundMessage>> _pendingSupersedable = new(StringComparer.Ordinal);

  // Esenciales pendientes. Se accede solo con _gate tomado.
  private int _essentialCount;

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
    EnqueueOutcome outcome;
    lock (_gate)
    {
      if (_completed)
      {
        return EnqueueOutcome.Closed;
      }

      outcome = message.Delivery == DeliveryClass.Essential
          ? EnqueueEssential(message)
          : EnqueueSupersedable(message);

      if (outcome == EnqueueOutcome.Saturated)
      {
        return outcome;
      }

      waiter = TakeWaiter();
    }

    // Se despierta afuera del lock para no retenerlo ni un instante más de lo necesario.
    waiter?.TrySetResult();
    return outcome;
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
          Forget(first.Value);
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

  public void DiscardPendingAndComplete(OutboundMessage farewell)
  {
    ArgumentNullException.ThrowIfNull(farewell);

    TaskCompletionSource? waiter;
    lock (_gate)
    {
      // Si un cierre anterior ya completó la cola, su Disconnecting ya está encolado y no se toca.
      if (_completed)
      {
        return;
      }

      // La despedida entra aunque la cola esté llena: los pendientes se van y no queda otro mensaje que proteger.
      _items.Clear();
      _pendingSupersedable.Clear();
      _items.AddLast(farewell);
      _essentialCount = farewell.Delivery == DeliveryClass.Essential ? 1 : 0;
      _completed = true;
      waiter = TakeWaiter();
    }

    waiter?.TrySetResult();
  }

  // Debe llamarse con _gate tomado.
  private EnqueueOutcome EnqueueEssential(OutboundMessage message)
  {
    // Los esenciales no usan el lugar reservado. El tope de la cola también se controla por si hubiera más de un tipo reemplazable.
    if (_essentialCount >= _capacity - ReservedForSupersedable || _items.Count >= _capacity)
    {
      return EnqueueOutcome.Saturated;
    }

    _items.AddLast(message);
    _essentialCount++;
    return EnqueueOutcome.Enqueued;
  }

  // Debe llamarse con _gate tomado.
  private EnqueueOutcome EnqueueSupersedable(OutboundMessage message)
  {
    bool superseded = false;
    if (_pendingSupersedable.Remove(message.Type, out var pending))
    {
      // El pendiente todavía no lo tomó el consumidor (si lo hubiera tomado, ya no estaría acá): se quita y el nuevo va al final.
      _items.Remove(pending);
      superseded = true;
    }
    else if (_items.Count >= _capacity)
    {
      // Con un solo tipo reemplazable no ocurre: el lugar reservado alcanza. Con más tipos, no se pasa de la capacidad.
      return EnqueueOutcome.Saturated;
    }

    _pendingSupersedable[message.Type] = _items.AddLast(message);
    return superseded ? EnqueueOutcome.Superseded : EnqueueOutcome.Enqueued;
  }

  // Debe llamarse con _gate tomado, cuando el consumidor tomó el mensaje.
  private void Forget(OutboundMessage message)
  {
    if (message.Delivery == DeliveryClass.Essential)
    {
      _essentialCount--;
    }
    else
    {
      _pendingSupersedable.Remove(message.Type);
    }
  }

  // Debe llamarse con _gate tomado.
  private TaskCompletionSource? TakeWaiter()
  {
    var waiter = _waiter;
    _waiter = null;
    return waiter;
  }
}
