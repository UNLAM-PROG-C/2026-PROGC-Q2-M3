using Rts.GameSession.Actions;
using Rts.GameSession.Events;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

/// <summary>Desconexiones antes y durante Running, ReadyTimeout y el lote de D-055 (contrato 07 §3, §6, §11).</summary>
public class DisconnectionTests : StateMachineTestBase
{
  [Fact]
  public void DisconnectWhileWaiting_FreesTheSlotWithoutActions()
  {
    SessionTransition transition = Run(Hello(ConnA), Closed(ConnA));

    Assert.Null(transition.State.Player1);
    Assert.Empty(transition.Actions);
  }

  [Fact]
  public void DisconnectWhileStarting_AbortsTheAttempt_AndTheRemainingPlayerKeepsItsSlot()
  {
    SessionTransition transition = Run([.. BothJoined, Ready(ConnB), Closed(ConnA)]);

    Assert.Equal(SessionStatus.WaitingForPlayers, transition.State.Status);
    Assert.Null(transition.State.Player1);
    Assert.Equal(PlayerId.Two, transition.State.FindByConnection(ConnB)?.Player);
    Assert.False(transition.State.Player2!.IsReady);
    Assert.Null(transition.State.CurrentInitialization);
    AssertActions(transition, Sent(ConnB, new StartAborted(Init1, StartAbortReason.OpponentLeft)));
  }

  [Fact]
  public void NewClientAfterAnAbort_TakesTheFreeSlot_WithABrandNewInitialization()
  {
    SessionTransition transition = Run([.. BothJoined, Closed(ConnA), Hello(ConnC)]);

    Assert.Equal(PlayerId.One, transition.State.FindByConnection(ConnC)?.Player);
    Assert.Equal(PlayerId.Two, transition.State.FindByConnection(ConnB)?.Player);
    AssertActions(
        transition,
        Sent(ConnC, new PlayerAssigned(Session, PlayerId.One)),
        new PrepareInitialState(Init2, new SessionRecipient(PlayerId.One, ConnC), new SessionRecipient(PlayerId.Two, ConnB)),
        new ScheduleReadyTimeout(Init2, ReadyTimeout));
  }

  [Fact]
  public void ReadyTimeout_WithOnePlayerReady_ExpelsTheOtherAndKeepsTheReadyOne()
  {
    SessionTransition transition = Run([.. BothJoined, Ready(ConnA), new ReadyTimeoutElapsed(Init1)]);

    Assert.Equal(SessionStatus.WaitingForPlayers, transition.State.Status);
    Assert.Null(transition.State.Player2);
    Assert.False(transition.State.Player1!.IsReady);
    AssertActions(
        transition,
        new CloseConnection(ConnB, SessionErrors.ReadyTimeoutExpired),
        Sent(ConnA, new StartAborted(Init1, StartAbortReason.ReadyTimeout)));
  }

  [Fact]
  public void ReadyTimeout_WithNobodyReady_ExpelsBothAndLeavesAnEmptySession()
  {
    SessionTransition transition = Run([.. BothJoined, new ReadyTimeoutElapsed(Init1)]);

    Assert.Empty(transition.State.OccupiedSlots);
    AssertActions(
        transition,
        new CloseConnection(ConnA, SessionErrors.ReadyTimeoutExpired),
        new CloseConnection(ConnB, SessionErrors.ReadyTimeoutExpired));
  }

  [Fact]
  public void ReadyTimeout_OfACancelledAttempt_IsIgnored()
  {
    SessionTransition transition = Run([.. BothJoined, Closed(ConnA), Hello(ConnC), new ReadyTimeoutElapsed(Init1)]);

    Assert.Equal(SessionStatus.Starting, transition.State.Status);
    Assert.Equal(Init2, transition.State.CurrentInitialization);
    Assert.Empty(transition.Actions);
  }

  [Fact]
  public void ReadyTimeout_AfterTheMatchStarted_IsIgnored()
  {
    SessionTransition transition = Run([.. MatchRunning, new ReadyTimeoutElapsed(Init1)]);

    Assert.Equal(SessionStatus.Running, transition.State.Status);
    Assert.Empty(transition.Actions);
  }

  [Fact]
  public void FirstDisconnectInRunning_RequestsStopWithoutFixingAResult()
  {
    SessionTransition transition = Run([.. MatchRunning, Closed(ConnA)]);

    Assert.Equal(SessionStatus.Running, transition.State.Status);
    Assert.True(transition.State.StopRequested);
    Assert.False(transition.State.AcceptsGameplay);
    Assert.Null(transition.State.Result);
    AssertActions(transition, new StopSimulation());
  }

  [Fact]
  public void PlayerOneDisconnects_PlayerTwoWinsOnlyAfterSimulationStopped()
  {
    SessionTransition transition = Run([.. MatchRunning, Closed(ConnA), Stopped()]);

    MatchResult expected = MatchResult.PlayerDisconnected(PlayerId.Two, FinalTick);
    Assert.Equal(SessionStatus.Ending, transition.State.Status);
    Assert.Equal(expected, transition.State.Result);
    AssertActions(transition, Sent(ConnB, new MatchEnded(Session, expected)), new ShutdownConnections());
  }

  [Fact]
  public void BothDisconnectInTheSameBatch_EndInATechnicalResultWithNoWinner()
  {
    SessionTransition second = Run([.. MatchRunning, Closed(ConnA), Closed(ConnB)]);
    SessionTransition ended = RunFrom(second.State, Stopped());

    Assert.Empty(second.Actions);
    Assert.Equal(MatchResult.BothPlayersDisconnected(FinalTick), ended.State.Result);
    AssertActions(ended, new ShutdownConnections());
  }

  [Fact]
  public void SimulationResultInsideTheBatch_PrevailsOverTheDisconnection()
  {
    MatchResult worldResult = MatchResult.BaseDestroyed(PlayerId.One, FinalTick);

    SessionTransition transition = Run([.. MatchRunning, Closed(ConnA), SimulationResult(worldResult)]);
    SessionTransition afterStop = RunFrom(transition.State, Stopped());

    Assert.Equal(worldResult, transition.State.Result);
    AssertActions(transition, Sent(ConnB, new MatchEnded(Session, worldResult)), new ShutdownConnections());
    Assert.Empty(afterStop.Actions);
    Assert.Equal(worldResult, afterStop.State.Result);
  }

  [Fact]
  public void SecondDisconnectInTheBatch_DoesNotAskToStopAgain()
  {
    SessionTransition transition = Run([.. MatchRunning, Closed(ConnA), Closed(ConnB), Closed(ConnA)]);

    Assert.Empty(transition.Actions);
    Assert.Equal(2, transition.State.PendingDisconnects.Count);
  }

  [Fact]
  public void CloseFromUnknownConnection_HasNoEffect()
  {
    SessionTransition transition = Run([.. MatchRunning, Closed(ConnC)]);

    Assert.False(transition.State.StopRequested);
    Assert.Empty(transition.Actions);
  }
}
