using Rts.GameSession.Model;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

public class StateTests
{
    private static readonly SessionId Session = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly ConnectionId ConnA = new(10);
    private static readonly ConnectionId ConnB = new(11);
    private static readonly InitializationId Init1 = InitializationId.First;

    private static PlayerSession P1(bool connected = true, bool ready = false) => new(PlayerId.One, ConnA, connected, ready);

    private static PlayerSession P2(bool connected = true, bool ready = false) => new(PlayerId.Two, ConnB, connected, ready);

    [Fact]
    public void Initial_IsEmptyWaitingForPlayers()
    {
        GameSessionState state = GameSessionState.Initial(Session);

        Assert.Equal(SessionStatus.WaitingForPlayers, state.Status);
        Assert.Empty(state.OccupiedSlots);
        Assert.False(state.AcceptsGameplay);
        Assert.Equal(InitializationId.First, state.NextInitialization);
        Assert.Null(state.Result);
    }

    [Fact]
    public void Waiting_WithBothSlotsOccupied_IsImpossible()
    {
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.WaitingForPlayers, P1(), P2(), null, Init1, false, default, false, null));
    }

    [Fact]
    public void Waiting_KeepsTheRemainingPlayerInItsOwnSlot()
    {
        // D-054: si P1 se fue antes de Running, P2 sigue siendo P2 y la plaza libre es la de P1.
        var state = new GameSessionState(Session, SessionStatus.WaitingForPlayers, null, P2(), null, Init1, false, default, false, null);

        Assert.Null(state.Player1);
        Assert.Equal(PlayerId.Two, state.FindByConnection(ConnB)?.Player);
        Assert.Equal(new InitializationId(2), state.NextInitialization);
    }

    [Fact]
    public void Starting_RequiresTheLatestInitialization()
    {
        var valid = new GameSessionState(Session, SessionStatus.Starting, P1(ready: true), P2(), Init1, Init1, false, default, false, null);

        Assert.Equal(Init1, valid.CurrentInitialization);
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.Starting, P1(), P2(), Init1, Init1.Next(), false, default, false, null));
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.Starting, P1(), null, Init1, Init1, false, default, false, null));
    }

    [Fact]
    public void Running_AcceptsGameplayUntilStopIsRequested()
    {
        var running = new GameSessionState(Session, SessionStatus.Running, P1(ready: true), P2(ready: true), Init1, Init1, false, default, false, null);
        var stopping = new GameSessionState(Session, SessionStatus.Running, P1(connected: false), P2(ready: true), Init1, Init1, true, [PlayerId.One], false, null);

        Assert.True(running.AcceptsGameplay);
        Assert.False(stopping.AcceptsGameplay);
    }

    [Fact]
    public void Running_DisconnectionMustBePendingAndStopRequested()
    {
        // Desconectado sin figurar en el lote.
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.Running, P1(connected: false), P2(ready: true), Init1, Init1, true, default, false, null));

        // En el lote, pero sin pedir la detención.
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.Running, P1(connected: false), P2(ready: true), Init1, Init1, false, [PlayerId.One], false, null));
    }

    [Fact]
    public void PendingDisconnects_MustBeOrdered()
    {
        Assert.Throws<ArgumentException>(() => new GameSessionState(
            Session, SessionStatus.Running, P1(connected: false), P2(connected: false), Init1, Init1, true, [PlayerId.Two, PlayerId.One], false, null));
    }

    [Fact]
    public void OnlyEndingAndEndedHaveResult()
    {
        MatchResult result = MatchResult.PlayerDisconnected(PlayerId.Two, new TickId(40));

        var ending = new GameSessionState(Session, SessionStatus.Ending, P1(connected: false), P2(ready: true), Init1, Init1, false, default, false, result);

        Assert.Equal(result, ending.Result);
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.Ending, P1(), P2(), Init1, Init1, false, default, false, null));
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.WaitingForPlayers, null, null, null, null, false, default, false, result));
    }

    [Fact]
    public void Slots_RejectWrongPlayerOrSharedConnection()
    {
        Assert.Throws<ArgumentException>(() => new GameSessionState(
            Session, SessionStatus.WaitingForPlayers, new PlayerSession(PlayerId.Two, ConnA, true, false), null, null, null, false, default, false, null));
        Assert.Throws<ArgumentException>(() => new GameSessionState(
            Session, SessionStatus.Starting, P1(), new PlayerSession(PlayerId.Two, ConnA, true, false), Init1, Init1, false, default, false, null));
    }

    [Fact]
    public void Shutdown_InRunningRequiresStopAndOnlyExistsInRunning()
    {
        // D-084: el apagado en Running primero detiene los ticks; el resultado se fija después.
        var stopping = new GameSessionState(Session, SessionStatus.Running, P1(ready: true), P2(ready: true), Init1, Init1, true, default, true, null);

        Assert.False(stopping.AcceptsGameplay);
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.Running, P1(ready: true), P2(ready: true), Init1, Init1, false, default, true, null));
        Assert.Throws<ArgumentException>(() =>
            new GameSessionState(Session, SessionStatus.WaitingForPlayers, P1(), null, null, null, false, default, true, null));
        Assert.Equal(MatchEndReason.ServerShutdown, MatchResult.ServerShutdown(null).Reason);
    }

    [Fact]
    public void PlayerSession_DisconnectedCannotBeReady()
    {
        Assert.Throws<ArgumentException>(() => new PlayerSession(PlayerId.One, ConnA, isConnected: false, isReady: true));
        Assert.Throws<ArgumentException>(() => new PlayerSession(new PlayerId(3), ConnA, true, false));
        Assert.Equal(P1(), PlayerSession.Registered(PlayerId.One, ConnA));
    }
}
