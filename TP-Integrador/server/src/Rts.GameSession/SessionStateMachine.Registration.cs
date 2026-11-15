using Rts.GameSession.Actions;
using Rts.GameSession.Model;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession;

/// <summary>Hello y Ready: asignación de plazas, preparación e inicio sincronizado (07 §3-5).</summary>
public sealed partial class SessionStateMachine
{
  /// <summary>
  /// Un Hello de una conexión ya registrada se rechaza; fuera de WaitingForPlayers, quien no tiene plaza se cierra con
  /// <c>MatchFull</c> (07 §2). En cualquier otro caso, la conexión pide plaza.
  /// </summary>
  private SessionTransition OnHello(GameSessionState state, ConnectionId connection)
  {
    if (state.FindByConnection(connection) is not null)
    {
      return Reject(state, connection, SessionErrors.AlreadyRegistered, null);
    }

    if (state.Status != SessionStatus.WaitingForPlayers)
    {
      return new(state, [new CloseConnection(connection, SessionErrors.MatchFull)]);
    }

    return Register(state, connection);
  }

  /// <summary>
  /// Ocupa la plaza libre (D-053, D-054): P1 si está vacía, P2 si no. Si con esto quedan las dos plazas ocupadas, empieza el
  /// intento de inicio; si no, solo se le avisa su plaza al jugador.
  /// </summary>
  private SessionTransition Register(GameSessionState state, ConnectionId connection)
  {
    PlayerId player = state.Player1 is null ? PlayerId.One : PlayerId.Two;
    bool completesBothSlots = state.OccupiedSlots.Any();
    SessionStateBuilder registered = SessionStateBuilder.From(state).WithSlot(player, PlayerSession.Registered(player, connection));
    SessionAction assigned = Send(connection, new PlayerAssigned(state.Session, player));
    return completesBothSlots ? BeginStarting(state, registered, assigned) : new(registered.Build(), [assigned]);
  }

  /// <summary>
  /// Dos plazas ocupadas: Starting con <c>NextInitialization</c>. Orden de acciones (07 §5): <c>PlayerAssigned</c> al nuevo →
  /// <c>PrepareInitialState</c> → <c>ScheduleReadyTimeout</c>, así cada cliente recibe su plaza antes que el InitialState.
  /// </summary>
  private SessionTransition BeginStarting(GameSessionState state, SessionStateBuilder registered, SessionAction assigned)
  {
    InitializationId initialization = state.NextInitialization;
    GameSessionState starting = registered.WithStatus(SessionStatus.Starting).WithNewInitialization(initialization).Build();
    var prepare = new PrepareInitialState(initialization, Recipient(starting.Player1!), Recipient(starting.Player2!));
    return new(starting, [assigned, prepare, new ScheduleReadyTimeout(initialization, _readyTimeout)]);
  }

  /// <summary>
  /// Ready válido marca al jugador como listo; con los dos listos, la partida arranca. Un Ready duplicado es idempotente y uno
  /// viejo o fuera de Starting se rechaza sin efecto (07 §3).
  /// </summary>
  private static SessionTransition OnReady(GameSessionState state, ConnectionId connection, InitializationId initialization)
  {
    if (FindReadyRejection(state, connection, initialization) is { } reason)
    {
      return Reject(state, connection, reason, initialization);
    }

    PlayerSession slot = state.FindByConnection(connection)!;
    if (slot.IsReady)
    {
      return SessionTransition.Unchanged(state);
    }

    GameSessionState marked = SessionStateBuilder.From(state).WithSlot(slot.Player, slot.WithReady(true)).Build();
    return marked.OccupiedSlots.All(s => s.IsReady) ? StartMatch(marked) : WithoutActions(marked);
  }

  /// <summary>Motivo de rechazo de un Ready, en el orden de precedencia de la tabla 07 §3, o <c>null</c> si es válido.</summary>
  private static ErrorCode? FindReadyRejection(GameSessionState state, ConnectionId connection, InitializationId initialization)
  {
    if (state.FindByConnection(connection) is null)
    {
      return SessionErrors.NotRegistered;
    }

    if (state.Status != SessionStatus.Starting)
    {
      return SessionErrors.NotStarting;
    }

    return initialization == state.CurrentInitialization ? null : SessionErrors.StaleInitialization;
  }

  /// <summary>
  /// Ambos listos: Running. <c>MatchStarted</c> se encola para los dos <b>antes</b> de <c>StartSimulation</c>, así ninguna
  /// publicación de gameplay se adelanta (07 §7).
  /// </summary>
  private static SessionTransition StartMatch(GameSessionState ready)
  {
    GameSessionState running = SessionStateBuilder.From(ready).WithStatus(SessionStatus.Running).Build();
    var started = new MatchStarted(ready.Session, ready.CurrentInitialization!.Value);
    return new(running, [Send(ready.Player1!.Connection, started), Send(ready.Player2!.Connection, started), new StartSimulation()]);
  }
}
