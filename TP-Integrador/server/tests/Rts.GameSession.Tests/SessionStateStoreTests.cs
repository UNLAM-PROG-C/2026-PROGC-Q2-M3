using Rts.GameSession.Model;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

/// <summary>Vista de sesión: lo que Networking lee desde sus hilos por <see cref="ISessionGate"/> (07 §8, 08 §5).</summary>
public class SessionStateStoreTests : StateMachineTestBase
{
  private const int PublishCount = 20_000;
  private const int ReaderCount = 4;

  [Fact]
  public void StartsWithTheInitialState_AndAnswersNobody()
  {
    var store = new SessionStateStore(GameSessionState.Initial(Session));

    Assert.Equal(SessionStatus.WaitingForPlayers, store.Status);
    Assert.False(store.AcceptsGameplay);
    Assert.Null(store.GetPlayer(ConnA));
    Assert.Null(store.GetConnection(PlayerId.One));
  }

  [Fact]
  public void AfterPublishingARunningMatch_ItAnswersWhoIsWho()
  {
    var store = new SessionStateStore(GameSessionState.Initial(Session));

    store.Publish(Run(MatchRunning).State);

    Assert.Equal(SessionStatus.Running, store.Status);
    Assert.True(store.AcceptsGameplay);
    Assert.Equal(PlayerId.One, store.GetPlayer(ConnA));
    Assert.Equal(PlayerId.Two, store.GetPlayer(ConnB));
    Assert.Equal(ConnA, store.GetConnection(PlayerId.One));
    Assert.Equal(ConnB, store.GetConnection(PlayerId.Two));
  }

  [Fact]
  public void ADisconnectedPlayer_IsInvisible_AndGameplayIsNoLongerAccepted()
  {
    var store = new SessionStateStore(Run([.. MatchRunning, Closed(ConnA)]).State);

    Assert.Null(store.GetPlayer(ConnA));
    Assert.Null(store.GetConnection(PlayerId.One));
    Assert.Equal(PlayerId.Two, store.GetPlayer(ConnB));
    Assert.False(store.AcceptsGameplay);
  }

  [Fact]
  public void AnUnregisteredConnection_HasNoPlayer()
  {
    var store = new SessionStateStore(Run(MatchRunning).State);

    Assert.Null(store.GetPlayer(ConnC));
  }

  [Fact]
  public void NullArguments_AreRejected()
  {
    var store = new SessionStateStore(GameSessionState.Initial(Session));

    Assert.Throws<ArgumentNullException>(() => new SessionStateStore(null!));
    Assert.Throws<ArgumentNullException>(() => store.Publish(null!));
  }

  [Fact]
  public async Task ConcurrentReaders_AlwaysSeeAWholePublishedState()
  {
    GameSessionState initial = GameSessionState.Initial(Session);
    GameSessionState starting = Run(BothJoined).State;
    GameSessionState running = Run(MatchRunning).State;
    var store = new SessionStateStore(initial);
    int torn = 0;

    Task writer = Task.Run(() => PublishAlternating(store, starting, running));
    Task[] readers = Enumerable.Range(0, ReaderCount).Select(_ => Task.Run(() => CountForeignStates(store, writer, [initial, starting, running], ref torn))).ToArray();
    await Task.WhenAll(readers.Append(writer));

    Assert.Equal(0, torn);
  }

  private static void PublishAlternating(SessionStateStore store, GameSessionState first, GameSessionState second)
  {
    for (int i = 0; i < PublishCount; i++)
    {
      store.Publish(i % 2 == 0 ? first : second);
    }
  }

  private static void CountForeignStates(SessionStateStore store, Task writer, GameSessionState[] published, ref int torn)
  {
    int local = 0;
    while (!writer.IsCompleted)
    {
      GameSessionState seen = store.Current;
      local += published.Any(state => ReferenceEquals(state, seen)) ? 0 : 1;
    }

    Interlocked.Add(ref torn, local);
  }
}
