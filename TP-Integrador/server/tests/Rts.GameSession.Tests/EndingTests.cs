using Rts.GameSession.Actions;
using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

/// <summary>Resultado único, apagado ordenado y estados terminales (contrato 07 §3, §6, D-084).</summary>
public class EndingTests : StateMachineTestBase
{
  private static readonly MatchResult WorldResult = MatchResult.BaseDestroyed(PlayerId.One, FinalTick);

  [Fact]
  public void SimulationResult_InRunning_StopsTicksAndAnnouncesTheEndToBoth()
  {
    SessionTransition transition = Run([.. MatchRunning, SimulationResult(WorldResult)]);

    Assert.Equal(SessionStatus.Ending, transition.State.Status);
    Assert.Equal(WorldResult, transition.State.Result);
    AssertActions(
        transition,
        new StopSimulation(),
        Sent(ConnA, new MatchEnded(Session, WorldResult)),
        Sent(ConnB, new MatchEnded(Session, WorldResult)),
        new ShutdownConnections());
  }

  [Fact]
  public void OnceFixed_TheResultNeverChanges()
  {
    GameSessionState ending = Run([.. MatchRunning, SimulationResult(WorldResult)]).State;

    SessionTransition lateDraw = Apply(ending, SimulationResult(MatchResult.Draw(FinalTick)));
    SessionTransition lateDisconnect = Apply(lateDraw.State, Closed(ConnB));

    Assert.Equal(WorldResult, lateDisconnect.State.Result);
    Assert.False(lateDisconnect.State.Player2!.IsConnected);
    Assert.Empty(lateDisconnect.Actions);
  }

  [Fact]
  public void ShutdownWhileWaiting_EndsWithAServerShutdownResult()
  {
    SessionTransition transition = Run(Hello(ConnA), new ShutdownRequested());

    Assert.Equal(SessionStatus.Ending, transition.State.Status);
    Assert.Equal(MatchResult.ServerShutdown(null), transition.State.Result);
    AssertActions(transition, new ShutdownConnections());
  }

  [Fact]
  public void ShutdownWhileRunning_StopsTicksFirst_AndFixesTheResultAfterwards()
  {
    SessionTransition requested = Run([.. MatchRunning, new ShutdownRequested()]);
    SessionTransition ended = RunFrom(requested.State, Stopped());

    Assert.Equal(SessionStatus.Running, requested.State.Status);
    Assert.False(requested.State.AcceptsGameplay);
    AssertActions(requested, new StopSimulation());
    Assert.Equal(MatchResult.ServerShutdown(FinalTick), ended.State.Result);
    AssertActions(
        ended,
        Sent(ConnA, new MatchEnded(Session, MatchResult.ServerShutdown(FinalTick))),
        Sent(ConnB, new MatchEnded(Session, MatchResult.ServerShutdown(FinalTick))),
        new ShutdownConnections());
  }

  [Fact]
  public void ShutdownAfterADisconnect_DoesNotAskToStopTwice_AndKeepsTheDisconnectResult()
  {
    SessionTransition requested = Run([.. MatchRunning, Closed(ConnA), new ShutdownRequested()]);
    SessionTransition ended = RunFrom(requested.State, Stopped());

    Assert.Empty(requested.Actions);
    Assert.Equal(MatchResult.PlayerDisconnected(PlayerId.Two, FinalTick), ended.State.Result);
  }

  [Fact]
  public void SimulationStopped_WithoutAStopRequest_IsIgnored()
  {
    SessionTransition transition = Run([.. MatchRunning, Stopped()]);

    Assert.Equal(SessionStatus.Running, transition.State.Status);
    Assert.Empty(transition.Actions);
  }

  [Fact]
  public void InitializationFailure_InStarting_EndsWithACriticalErrorWithoutFinalTick()
  {
    SessionTransition transition = Run([.. BothJoined, new InitializationFailed("sin mapa")]);

    MatchResult expected = MatchResult.CriticalError(null);
    Assert.Equal(expected, transition.State.Result);
    AssertActions(
        transition,
        Sent(ConnA, new MatchEnded(Session, expected)),
        Sent(ConnB, new MatchEnded(Session, expected)),
        new ShutdownConnections());
  }

  [Fact]
  public void ShutdownCompleted_MovesToEnded_AndCompletesTheSessionWithTheResult()
  {
    SessionTransition transition = Run([.. MatchRunning, SimulationResult(WorldResult), new ShutdownCompleted()]);

    Assert.Equal(SessionStatus.Ended, transition.State.Status);
    AssertActions(transition, new CompleteSession(WorldResult));
  }

  [Fact]
  public void AfterEnded_NothingHasEffect_AndNewClientsAreClosed()
  {
    SessionTransition ended = Run([.. MatchRunning, SimulationResult(WorldResult), new ShutdownCompleted()]);

    SessionTransition again = Apply(ended.State, new ShutdownCompleted());
    SessionTransition newcomer = Apply(ended.State, Hello(ConnC));

    Assert.Equal(ended.State, again.State);
    Assert.Empty(again.Actions);
    AssertActions(newcomer, new CloseConnection(ConnC, SessionErrors.MatchFull));
  }
}
