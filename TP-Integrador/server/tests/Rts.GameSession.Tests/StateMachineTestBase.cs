using Rts.GameSession.Actions;
using Rts.GameSession.Configuration;
using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

/// <summary>
/// Utilidades para probar la máquina de estados como lo pide 07 §10: con secuencias de eventos controladas, sin sockets, hilos
/// ni relojes. <see cref="Run"/> aplica los eventos en orden desde el estado inicial y devuelve la última transición.
/// </summary>
public abstract class StateMachineTestBase
{
  protected const int QueueCapacity = 64;

  protected static readonly SessionId Session = new(Guid.Parse("00000000-0000-0000-0000-000000000003"));
  protected static readonly ConnectionId ConnA = new(10);
  protected static readonly ConnectionId ConnB = new(11);
  protected static readonly ConnectionId ConnC = new(12);
  protected static readonly InitializationId Init1 = InitializationId.First;
  protected static readonly InitializationId Init2 = Init1.Next();
  protected static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
  protected static readonly TickId FinalTick = new(40);

  /// <summary>Dos clientes registrados: la sesión queda en Starting con la inicialización 1.</summary>
  protected static readonly SessionEvent[] BothJoined = [Hello(ConnA), Hello(ConnB)];

  /// <summary>Dos clientes registrados y listos: la partida está en Running.</summary>
  protected static readonly SessionEvent[] MatchRunning = [.. BothJoined, Ready(ConnA), Ready(ConnB)];

  private readonly SessionStateMachine _machine = new(new GameSessionConfig { ReadyTimeout = ReadyTimeout, EventQueueCapacity = QueueCapacity });

  protected SessionTransition Run(params SessionEvent[] events) => RunFrom(GameSessionState.Initial(Session), events);

  protected SessionTransition RunFrom(GameSessionState start, params SessionEvent[] events)
  {
    SessionTransition transition = SessionTransition.Unchanged(start);
    foreach (SessionEvent sessionEvent in events)
    {
      transition = _machine.Apply(transition.State, sessionEvent);
    }

    return transition;
  }

  protected SessionTransition Apply(GameSessionState state, SessionEvent sessionEvent) => _machine.Apply(state, sessionEvent);

  protected static SessionEvent Hello(ConnectionId connection) => new ClientHelloReceived(connection);

  protected static SessionEvent Ready(ConnectionId connection) => new ClientReadyReceived(connection, Init1);

  protected static SessionEvent ReadyFor(ConnectionId connection, InitializationId initialization) => new ClientReadyReceived(connection, initialization);

  protected static SessionEvent Closed(ConnectionId connection) => new ConnectionClosed(connection);

  protected static SessionEvent Stopped() => new SimulationStopped(FinalTick);

  protected static SessionEvent SimulationResult(Rts.Shared.Match.MatchResult result) => new SimulationResultReceived(result);

  protected static SessionAction Sent(ConnectionId connection, ISessionMessage message) => new SendSessionMessage(connection, message);

  /// <summary>Verifica las acciones de la transición, en orden exacto: el orden es parte del contrato (07 §3).</summary>
  protected static void AssertActions(SessionTransition transition, params SessionAction[] expected) =>
      Assert.Equal<SessionAction>(expected, transition.Actions);
}
