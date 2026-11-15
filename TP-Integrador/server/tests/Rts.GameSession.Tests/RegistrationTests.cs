using Rts.GameSession.Actions;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

/// <summary>Plazas, handshake e inicio sincronizado (07 §4-5, contrato 07 §11).</summary>
public class RegistrationTests : StateMachineTestBase
{
  [Fact]
  public void FirstHello_GetsPlayerOne_AndTheSessionKeepsWaitingWithoutTimeout()
  {
    SessionTransition transition = Run(Hello(ConnA));

    Assert.Equal(SessionStatus.WaitingForPlayers, transition.State.Status);
    Assert.Equal(PlayerId.One, transition.State.Player1?.Player);
    AssertActions(transition, Sent(ConnA, new PlayerAssigned(Session, PlayerId.One)));
  }

  [Fact]
  public void SecondHello_GetsPlayerTwo_AndStartsWithActionsInOrder()
  {
    SessionTransition transition = Run(BothJoined);

    Assert.Equal(SessionStatus.Starting, transition.State.Status);
    Assert.Equal(Init1, transition.State.CurrentInitialization);
    AssertActions(
        transition,
        Sent(ConnB, new PlayerAssigned(Session, PlayerId.Two)),
        new PrepareInitialState(Init1, new SessionRecipient(PlayerId.One, ConnA), new SessionRecipient(PlayerId.Two, ConnB)),
        new ScheduleReadyTimeout(Init1, ReadyTimeout));
  }

  [Fact]
  public void ThirdClient_IsClosedWithMatchFull_AndDoesNotChangeTheState()
  {
    SessionTransition transition = Run([.. BothJoined, Hello(ConnC)]);

    Assert.Equal(SessionStatus.Starting, transition.State.Status);
    Assert.Null(transition.State.FindByConnection(ConnC));
    AssertActions(transition, new CloseConnection(ConnC, SessionErrors.MatchFull));
  }

  [Fact]
  public void DuplicateHello_IsRejected_AndKeepsTheSlot()
  {
    SessionTransition transition = Run(Hello(ConnA), Hello(ConnA));

    Assert.Equal(PlayerId.One, transition.State.FindByConnection(ConnA)?.Player);
    AssertActions(transition, Sent(ConnA, new SessionRequestRejected(SessionErrors.AlreadyRegistered, null)));
  }

  [Fact]
  public void OneReady_DoesNotStartTheMatch()
  {
    SessionTransition transition = Run([.. BothJoined, Ready(ConnA)]);

    Assert.Equal(SessionStatus.Starting, transition.State.Status);
    Assert.True(transition.State.Player1!.IsReady);
    Assert.Empty(transition.Actions);
  }

  [Fact]
  public void BothReady_StartMatchStartedForBothBeforeStartSimulation()
  {
    SessionTransition transition = Run(MatchRunning);

    Assert.Equal(SessionStatus.Running, transition.State.Status);
    Assert.True(transition.State.AcceptsGameplay);
    AssertActions(
        transition,
        Sent(ConnA, new MatchStarted(Session, Init1)),
        Sent(ConnB, new MatchStarted(Session, Init1)),
        new StartSimulation());
  }

  [Fact]
  public void DuplicateReady_IsIdempotent_AndNeverStartsTwice()
  {
    SessionTransition transition = Run([.. BothJoined, Ready(ConnA), Ready(ConnA)]);

    Assert.Equal(SessionStatus.Starting, transition.State.Status);
    Assert.Empty(transition.Actions);
  }

  [Fact]
  public void StaleReady_IsRejected_AndDoesNotMarkThePlayerReady()
  {
    SessionTransition transition = Run([.. BothJoined, ReadyFor(ConnA, Init2)]);

    Assert.False(transition.State.Player1!.IsReady);
    AssertActions(transition, Sent(ConnA, new SessionRequestRejected(SessionErrors.StaleInitialization, Init2)));
  }

  [Fact]
  public void ReadyFromUnknownConnection_IsRejectedAsNotRegistered()
  {
    SessionTransition transition = Run(Hello(ConnA), Ready(ConnC));

    AssertActions(transition, Sent(ConnC, new SessionRequestRejected(SessionErrors.NotRegistered, Init1)));
  }

  [Fact]
  public void ReadyWhileWaiting_IsRejectedAsNotStarting()
  {
    SessionTransition transition = Run(Hello(ConnA), Ready(ConnA));

    Assert.False(transition.State.Player1!.IsReady);
    AssertActions(transition, Sent(ConnA, new SessionRequestRejected(SessionErrors.NotStarting, Init1)));
  }

  [Fact]
  public void ReadyAfterTheMatchStarted_IsRejectedAndDoesNotRestartIt()
  {
    SessionTransition transition = Run([.. MatchRunning, Ready(ConnA)]);

    Assert.Equal(SessionStatus.Running, transition.State.Status);
    AssertActions(transition, Sent(ConnA, new SessionRequestRejected(SessionErrors.NotStarting, Init1)));
  }
}
