using Rts.GameSession.Actions;
using Rts.GameSession.Configuration;
using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Collections;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession;

/// <summary>
/// Implementación de <see cref="ISessionStateMachine"/>: la tabla de transiciones del contrato 07 §3 como reductor puro
/// <c>(estado, evento) → (estado nuevo, acciones)</c>. Cada tipo de evento tiene su manejador en un archivo parcial:
/// <list type="bullet">
/// <item><c>SessionStateMachine.Registration.cs</c>: Hello y Ready (plazas e inicio, 07 §4-5).</item>
/// <item><c>SessionStateMachine.Disconnection.cs</c>: cierres de conexión y ReadyTimeout (07 §5-6).</item>
/// <item><c>SessionStateMachine.Ending.cs</c>: resultados, detención y apagado (07 §6, D-084).</item>
/// </list>
/// No hace I/O, no usa reloj ni hilos, y nunca lanza por eventos tardíos o desconocidos: los ignora o los rechaza.
/// </summary>
public sealed partial class SessionStateMachine : ISessionStateMachine
{
  private readonly TimeSpan _readyTimeout;

  /// <exception cref="ArgumentException">Si la configuración no es válida.</exception>
  public SessionStateMachine(GameSessionConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    _readyTimeout = config.ReadyTimeout;
  }

  /// <inheritdoc/>
  public GameSessionState CreateInitial(SessionId session) => GameSessionState.Initial(session);

  /// <inheritdoc/>
  public SessionTransition Apply(GameSessionState state, SessionEvent sessionEvent)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(sessionEvent);
    return Dispatch(state, sessionEvent);
  }

  private SessionTransition Dispatch(GameSessionState state, SessionEvent sessionEvent) => sessionEvent switch
  {
    ClientHelloReceived hello => OnHello(state, hello.Connection),
    ClientReadyReceived ready => OnReady(state, ready.Connection, ready.Initialization),
    ConnectionClosed closed => OnConnectionClosed(state, closed.Connection),
    ReadyTimeoutElapsed timeout => OnReadyTimeout(state, timeout.Initialization),
    InitializationFailed => OnInitializationFailed(state),
    SimulationResultReceived simulationResult => OnSimulationResult(state, simulationResult.Result),
    SimulationStopped stopped => OnSimulationStopped(state, stopped.LastConfirmedTick),
    ShutdownRequested => OnShutdownRequested(state),
    ShutdownCompleted => OnShutdownCompleted(state),
    _ => SessionTransition.Unchanged(state),
  };

  /// <summary>Estado nuevo sin efectos externos (por ejemplo, liberar una plaza en WaitingForPlayers).</summary>
  private static SessionTransition WithoutActions(GameSessionState state) => new(state, EquatableArray<SessionAction>.Empty);

  private static SessionAction Send(ConnectionId connection, ISessionMessage message) => new SendSessionMessage(connection, message);

  /// <summary>La solicitud no tuvo efecto: el estado no cambia y el cliente recibe el motivo, sin cerrar la conexión.</summary>
  private static SessionTransition Reject(GameSessionState state, ConnectionId connection, ErrorCode reason, InitializationId? initialization) =>
      new(state, [Send(connection, new SessionRequestRejected(reason, initialization))]);

  private static SessionRecipient Recipient(PlayerSession slot) => new(slot.Player, slot.Connection);

  /// <summary>Vuelta a WaitingForPlayers: la inicialización vigente deja de valer (07 §4).</summary>
  private static SessionStateBuilder BackToWaiting(GameSessionState state) =>
      SessionStateBuilder.From(state).WithStatus(SessionStatus.WaitingForPlayers).WithoutCurrentInitialization();
}
