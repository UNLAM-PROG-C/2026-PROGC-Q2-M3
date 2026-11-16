using Rts.GameSession.Configuration;
using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Match;

namespace Rts.GameSession.Tests;

/// <summary>
/// Propiedades de la máquina completa: nunca lanza ante eventos inesperados, es determinista y no admite configuración inválida
/// (contrato 07 §3).
/// </summary>
public class InvariantTests : StateMachineTestBase
{
  [Fact]
  public void EveryEventInEveryState_NeverThrows_AndIsDeterministic()
  {
    foreach (GameSessionState state in AllStates())
    {
      foreach (SessionEvent sessionEvent in AllEvents())
      {
        SessionTransition first = Apply(state, sessionEvent);
        SessionTransition second = Apply(state, sessionEvent);

        Assert.Equal(first, second);
      }
    }
  }

  [Fact]
  public void InvalidConfiguration_IsRejectedWhenBuildingTheMachine()
  {
    var invalid = new GameSessionConfig { ReadyTimeout = TimeSpan.Zero, EventQueueCapacity = QueueCapacity };

    Assert.Throws<ArgumentException>(() => new SessionStateMachine(invalid));
  }

  [Fact]
  public void NullArguments_AreRejected()
  {
    Assert.Throws<ArgumentNullException>(() => Apply(null!, Hello(ConnA)));
    Assert.Throws<ArgumentNullException>(() => Apply(GameSessionState.Initial(Session), null!));
  }

  [Fact]
  public void CreateInitial_IsAnEmptyWaitingSession()
  {
    var machine = new SessionStateMachine(new GameSessionConfig { ReadyTimeout = ReadyTimeout, EventQueueCapacity = QueueCapacity });

    Assert.Equal(GameSessionState.Initial(Session), machine.CreateInitial(Session));
  }

  private IEnumerable<GameSessionState> AllStates()
  {
    MatchResult draw = MatchResult.Draw(FinalTick);
    yield return GameSessionState.Initial(Session);
    yield return Run(Hello(ConnA)).State;
    yield return Run(BothJoined).State;
    yield return Run(MatchRunning).State;
    yield return Run([.. MatchRunning, Closed(ConnA)]).State;
    yield return Run([.. MatchRunning, new ShutdownRequested()]).State;
    yield return Run([.. MatchRunning, SimulationResult(draw)]).State;
    yield return Run([.. MatchRunning, SimulationResult(draw), new ShutdownCompleted()]).State;
  }

  private static IEnumerable<SessionEvent> AllEvents() =>
  [
    Hello(ConnA),
    Hello(ConnC),
    Ready(ConnA),
    ReadyFor(ConnC, Init2),
    Closed(ConnA),
    Closed(ConnC),
    new ReadyTimeoutElapsed(Init1),
    new ReadyTimeoutElapsed(Init2),
    new InitializationFailed("falla"),
    SimulationResult(MatchResult.Draw(FinalTick)),
    new SimulationStopped(null),
    Stopped(),
    new ShutdownRequested(),
    new ShutdownCompleted(),
  ];
}
