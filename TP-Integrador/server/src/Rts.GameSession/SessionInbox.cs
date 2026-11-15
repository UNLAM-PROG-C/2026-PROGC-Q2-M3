using System.Threading.Channels;
using Rts.GameSession.Events;
using Rts.Shared.Ids;
using Rts.Shared.Match;

namespace Rts.GameSession;

/// <summary>
/// Implementación de <see cref="ISessionInbox"/>: la única entrada de eventos de sesión (07 §8, D-082).
/// <list type="bullet">
/// <item>Varios productores (los dos receptores de Networking, Simulation y el propio host) y <b>un solo consumidor</b>.</item>
/// <item>Solo <c>Hello</c> y <c>ClientReady</c> están acotados por la capacidad. Los cierres, los resultados y los avisos
/// internos nunca se descartan: hay a lo sumo uno por conexión o por intento, así que su cantidad ya está acotada (D-059).</item>
/// <item>No bloquea a quien publica ni toma locks: el contador de eventos acotados se reserva con una operación atómica.</item>
/// </list>
/// </summary>
public sealed class SessionInbox : ISessionInbox
{
  private readonly Channel<SessionEvent> _events = Channel.CreateUnbounded<SessionEvent>(
      new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

  private readonly int _capacity;
  private int _boundedPending;

  /// <param name="eventQueueCapacity">Máximo de Hello y ClientReady pendientes (<c>GameSessionConfig.EventQueueCapacity</c>).</param>
  public SessionInbox(int eventQueueCapacity)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventQueueCapacity);
    _capacity = eventQueueCapacity;
  }

  /// <summary>Cantidad de Hello y ClientReady que esperan ser consumidos. Para pruebas y métricas.</summary>
  internal int BoundedPending => Volatile.Read(ref _boundedPending);

  /// <inheritdoc/>
  public bool TryPostHello(ConnectionId connection) => TryPostBounded(new ClientHelloReceived(connection));

  /// <inheritdoc/>
  public bool TryPostClientReady(ConnectionId connection, InitializationId initialization) =>
      TryPostBounded(new ClientReadyReceived(connection, initialization));

  /// <inheritdoc/>
  public void PostConnectionClosed(ConnectionId connection) => PostInternal(new ConnectionClosed(connection));

  /// <inheritdoc/>
  public void OnSimulationResult(MatchResult result)
  {
    ArgumentNullException.ThrowIfNull(result);
    PostInternal(new SimulationResultReceived(result));
  }

  /// <inheritdoc/>
  /// <exception cref="ArgumentException">Para Hello y ClientReady: esos entran por <c>TryPost*</c>, que respeta la capacidad.</exception>
  public void PostInternal(SessionEvent sessionEvent)
  {
    ArgumentNullException.ThrowIfNull(sessionEvent);
    if (IsBounded(sessionEvent))
    {
      throw new ArgumentException("Hello y ClientReady deben publicarse con TryPost*, que respeta la capacidad.", nameof(sessionEvent));
    }

    _events.Writer.TryWrite(sessionEvent);
  }

  /// <inheritdoc/>
  public async ValueTask<SessionEvent> ReadAsync(CancellationToken cancellationToken)
  {
    SessionEvent sessionEvent = await _events.Reader.ReadAsync(cancellationToken);
    if (IsBounded(sessionEvent))
    {
      Interlocked.Decrement(ref _boundedPending);
    }

    return sessionEvent;
  }

  private static bool IsBounded(SessionEvent sessionEvent) => sessionEvent is ClientHelloReceived or ClientReadyReceived;

  /// <summary>Reserva un lugar y encola. Si la cola acotada está llena, no encola y devuelve <c>false</c> sin esperar.</summary>
  private bool TryPostBounded(SessionEvent sessionEvent)
  {
    if (!TryReserve())
    {
      return false;
    }

    _events.Writer.TryWrite(sessionEvent);
    return true;
  }

  /// <summary>
  /// Reserva atómica de un lugar: compara y cambia el contador solo si nadie lo tocó entre la lectura y la escritura. Así, aunque
  /// dos receptores publiquen a la vez, nunca se supera la capacidad (y nunca se rechaza a alguien habiendo lugar).
  /// </summary>
  private bool TryReserve()
  {
    while (true)
    {
      int pending = Volatile.Read(ref _boundedPending);
      if (pending >= _capacity)
      {
        return false;
      }

      if (Interlocked.CompareExchange(ref _boundedPending, pending + 1, pending) == pending)
      {
        return true;
      }
    }
  }
}
