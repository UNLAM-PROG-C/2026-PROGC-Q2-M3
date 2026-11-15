using Rts.GameSession.Actions;
using Rts.GameSession.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession;

/// <summary>Cierres de conexión y vencimiento de <c>ReadyTimeout</c> (07 §3, §5-6).</summary>
public sealed partial class SessionStateMachine
{
  /// <summary>
  /// Efecto de un cierre según el estado. Una conexión sin plaza (un tercero, un expulsado, un cliente sin Hello) no cambia
  /// nada. Antes de Running una desconexión libera la plaza; en Running no se fija resultado: se anota en el lote (D-055).
  /// </summary>
  private static SessionTransition OnConnectionClosed(GameSessionState state, ConnectionId connection)
  {
    PlayerSession? slot = state.FindByConnection(connection);
    return slot is null ? SessionTransition.Unchanged(state) : state.Status switch
    {
      SessionStatus.WaitingForPlayers => ReleaseSlot(state, slot),
      SessionStatus.Starting => AbortStarting(state, slot),
      SessionStatus.Running => DisconnectInRunning(state, slot),
      _ => MarkDisconnected(state, slot),
    };
  }

  /// <summary>WaitingForPlayers: la plaza queda libre para el próximo Hello. No hay a quién avisar.</summary>
  private static SessionTransition ReleaseSlot(GameSessionState state, PlayerSession slot) =>
      WithoutActions(SessionStateBuilder.From(state).WithSlot(slot.Player, null).Build());

  /// <summary>
  /// Starting: se cancela el intento. El otro jugador conserva su plaza (D-054), pierde su Ready y recibe
  /// <c>StartAborted(OpponentLeft)</c> (D-057); la inicialización deja de ser vigente (07 §4).
  /// </summary>
  private static SessionTransition AbortStarting(GameSessionState state, PlayerSession leaving)
  {
    PlayerSession remaining = state.OccupiedSlots.Single(slot => slot.Player != leaving.Player);
    InitializationId aborted = state.CurrentInitialization!.Value;
    GameSessionState waiting = BackToWaiting(state).WithSlot(leaving.Player, null).WithSlot(remaining.Player, remaining.WithReady(false)).Build();
    return new(waiting, [Send(remaining.Connection, new StartAborted(aborted, StartAbortReason.OpponentLeft))]);
  }

  /// <summary>
  /// Running: el jugador pasa a desconectado y entra al lote. La primera desconexión además pide detener los ticks
  /// (<see cref="StopSimulation"/>); las siguientes, hasta que termine el tick en curso, solo se suman al lote.
  /// </summary>
  private static SessionTransition DisconnectInRunning(GameSessionState state, PlayerSession leaving)
  {
    if (!leaving.IsConnected)
    {
      return SessionTransition.Unchanged(state);
    }

    GameSessionState batched = SessionStateBuilder.From(state)
        .WithSlot(leaving.Player, leaving.AsDisconnected())
        .AddPendingDisconnect(leaving.Player)
        .WithStopRequested(true)
        .Build();
    return state.StopRequested ? WithoutActions(batched) : new(batched, [new StopSimulation()]);
  }

  /// <summary>Ending y Ended: el resultado ya está fijado y no cambia. Solo se anota que el jugador ya no está conectado.</summary>
  private static SessionTransition MarkDisconnected(GameSessionState state, PlayerSession leaving) =>
      leaving.IsConnected
          ? WithoutActions(SessionStateBuilder.From(state).WithSlot(leaving.Player, leaving.AsDisconnected()).Build())
          : SessionTransition.Unchanged(state);

  /// <summary>
  /// Venció el plazo de Ready (DECIDIDO D-056): se expulsa a quien no confirmó, y quien sí confirmó se queda con su Ready
  /// reiniciado. Si el timer es de un intento que ya no es el vigente, se ignora (07 §4).
  /// </summary>
  private static SessionTransition OnReadyTimeout(GameSessionState state, InitializationId initialization)
  {
    if (state.Status != SessionStatus.Starting || initialization != state.CurrentInitialization)
    {
      return SessionTransition.Unchanged(state);
    }

    PlayerSession[] expelled = state.OccupiedSlots.Where(slot => !slot.IsReady).ToArray();
    PlayerSession[] kept = state.OccupiedSlots.Where(slot => slot.IsReady).ToArray();
    return new(StateAfterTimeout(state, expelled, kept), ActionsAfterTimeout(initialization, expelled, kept));
  }

  /// <summary>Vuelta a WaitingForPlayers: los expulsados liberan su plaza y los que se quedan pierden su Ready.</summary>
  private static GameSessionState StateAfterTimeout(GameSessionState state, PlayerSession[] expelled, PlayerSession[] kept)
  {
    SessionStateBuilder waiting = BackToWaiting(state);
    foreach (PlayerSession slot in expelled)
    {
      waiting.WithSlot(slot.Player, null);
    }

    foreach (PlayerSession slot in kept)
    {
      waiting.WithSlot(slot.Player, slot.WithReady(false));
    }

    return waiting.Build();
  }

  /// <summary>Primero se cierra a cada expulsado (<c>ReadyTimeoutExpired</c>) y después se avisa a cada jugador que se queda.</summary>
  private static EquatableArray<SessionAction> ActionsAfterTimeout(InitializationId initialization, PlayerSession[] expelled, PlayerSession[] kept)
  {
    var actions = new List<SessionAction>();
    actions.AddRange(expelled.Select(slot => new CloseConnection(slot.Connection, SessionErrors.ReadyTimeoutExpired)));
    actions.AddRange(kept.Select(slot => Send(slot.Connection, new StartAborted(initialization, StartAbortReason.ReadyTimeout))));
    return actions.ToEquatableArray();
  }
}
