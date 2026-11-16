using Rts.GameSession.Events;
using Rts.Shared.Ids;
using Rts.Shared.Match;

namespace Rts.GameSession.Tests;

/// <summary>Buzón de eventos: cota solo para Hello y Ready, nada descartable se pierde y varios productores no se pisan (07 §8).</summary>
public class SessionInboxTests
{
  private const int SmallCapacity = 2;
  private const int ProducerCount = 8;
  private const int PostsPerProducer = 500;
  private const int ConnectionIdStride = 1000;
  private const int HelloAttemptsPerProducer = 100;
  private const int ConcurrentCapacity = 50;
  private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
  private static readonly ConnectionId Conn = new(1);
  private static readonly InitializationId Init = InitializationId.First;

  [Fact]
  public void HelloAndReady_AreBoundedByTheCapacity()
  {
    var inbox = new SessionInbox(SmallCapacity);

    Assert.True(inbox.TryPostHello(Conn));
    Assert.True(inbox.TryPostClientReady(Conn, Init));
    Assert.False(inbox.TryPostHello(Conn));
    Assert.False(inbox.TryPostClientReady(Conn, Init));
    Assert.Equal(SmallCapacity, inbox.BoundedPending);
  }

  [Fact]
  public async Task ConsumingAnEvent_FreesCapacityForTheNextOne()
  {
    var inbox = new SessionInbox(SmallCapacity);
    inbox.TryPostHello(Conn);
    inbox.TryPostHello(Conn);
    Assert.False(inbox.TryPostHello(Conn));

    await ReadAsync(inbox);

    Assert.True(inbox.TryPostHello(Conn));
  }

  [Fact]
  public async Task ClosesResultsAndInternalEvents_AreNeverDropped_EvenWhenTheBoundedQueueIsFull()
  {
    var inbox = new SessionInbox(SmallCapacity);
    inbox.TryPostHello(Conn);
    inbox.TryPostHello(Conn);

    for (int i = 0; i < PostsPerProducer; i++)
    {
      inbox.PostConnectionClosed(new ConnectionId(i));
    }

    inbox.OnSimulationResult(MatchResult.Draw(new TickId(1)));
    inbox.PostInternal(new ShutdownCompleted());

    List<SessionEvent> received = await ReadAllAsync(inbox, SmallCapacity + PostsPerProducer + 2);
    Assert.Equal(PostsPerProducer, received.OfType<ConnectionClosed>().Count());
    Assert.IsType<SimulationResultReceived>(received[^2]);
    Assert.IsType<ShutdownCompleted>(received[^1]);
  }

  [Fact]
  public async Task OneProducer_KeepsItsOrder()
  {
    var inbox = new SessionInbox(SmallCapacity);
    inbox.TryPostHello(Conn);
    inbox.PostConnectionClosed(Conn);
    inbox.PostInternal(new ReadyTimeoutElapsed(Init));

    List<SessionEvent> received = await ReadAllAsync(inbox, 3);

    Assert.Equal<SessionEvent>([new ClientHelloReceived(Conn), new ConnectionClosed(Conn), new ReadyTimeoutElapsed(Init)], received);
  }

  [Fact]
  public void PostInternal_RejectsHelloAndReady_BecauseTheyMustRespectTheCapacity()
  {
    var inbox = new SessionInbox(SmallCapacity);

    Assert.Throws<ArgumentException>(() => inbox.PostInternal(new ClientHelloReceived(Conn)));
    Assert.Throws<ArgumentException>(() => inbox.PostInternal(new ClientReadyReceived(Conn, Init)));
    Assert.Equal(0, inbox.BoundedPending);
  }

  [Fact]
  public async Task ReadAsync_WaitsUntilAnEventArrives()
  {
    var inbox = new SessionInbox(SmallCapacity);

    Task<SessionEvent> pending = ReadAsync(inbox);
    Assert.False(pending.IsCompleted);
    inbox.PostConnectionClosed(Conn);

    Assert.Equal(new ConnectionClosed(Conn), await pending);
  }

  [Fact]
  public async Task ReadAsync_WithACancelledToken_Throws()
  {
    var inbox = new SessionInbox(SmallCapacity);
    using var cancelled = new CancellationTokenSource();
    await cancelled.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inbox.ReadAsync(cancelled.Token).AsTask());
  }

  [Fact]
  public void Constructor_RejectsANonPositiveCapacity()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new SessionInbox(0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new SessionInbox(-1));
  }

  [Fact]
  public async Task ManyProducersPostingHelloAtOnce_NeverExceedTheCapacity()
  {
    var inbox = new SessionInbox(ConcurrentCapacity);
    int accepted = 0;

    await Task.WhenAll(Enumerable.Range(0, ProducerCount).Select(_ => Task.Run(() =>
    {
      for (int i = 0; i < HelloAttemptsPerProducer; i++)
      {
        if (inbox.TryPostHello(Conn))
        {
          Interlocked.Increment(ref accepted);
        }
      }
    })));

    Assert.Equal(ConcurrentCapacity, accepted);
    Assert.Equal(ConcurrentCapacity, inbox.BoundedPending);
  }

  [Fact]
  public async Task ManyProducersPostingCloses_LoseNothing_AndKeepEachProducersOrder()
  {
    var inbox = new SessionInbox(SmallCapacity);

    await Task.WhenAll(Enumerable.Range(0, ProducerCount).Select(producer => Task.Run(() =>
    {
      for (int i = 0; i < PostsPerProducer; i++)
      {
        inbox.PostConnectionClosed(new ConnectionId((producer * ConnectionIdStride) + i));
      }
    })));

    List<SessionEvent> received = await ReadAllAsync(inbox, ProducerCount * PostsPerProducer);
    long[] ids = received.Cast<ConnectionClosed>().Select(e => e.Connection.Value).ToArray();
    Assert.Equal(ids.Length, ids.Distinct().Count());
    Assert.All(ids.GroupBy(id => id / ConnectionIdStride), producer => Assert.Equal(producer.Order(), producer));
  }

  private static async Task<SessionEvent> ReadAsync(SessionInbox inbox)
  {
    using var timeout = new CancellationTokenSource(ReadTimeout);
    return await inbox.ReadAsync(timeout.Token);
  }

  private static async Task<List<SessionEvent>> ReadAllAsync(SessionInbox inbox, int count)
  {
    using var timeout = new CancellationTokenSource(ReadTimeout);
    var received = new List<SessionEvent>(count);
    for (int i = 0; i < count; i++)
    {
      received.Add(await inbox.ReadAsync(timeout.Token));
    }

    return received;
  }
}
