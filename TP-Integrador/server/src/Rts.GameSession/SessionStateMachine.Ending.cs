using Rts.GameSession.Actions;
using Rts.GameSession.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession;

/// <summary>Resultado oficial, detención de los ticks y apagado (07 §3, §6, D-055, D-084).</summary>
public sealed partial class SessionStateMachine
{
  /// <summary>Cantidad de jugadores de una partida: si todos están en el lote de desconexiones, nadie gana.</summary>
  private const int PlayersPerMatch = 2;

  /// <summary><c>InitializeMatch</c> o la publicación del InitialState fallaron en Starting: final técnico sin FinalTick (D-060).</summary>
  private static SessionTransition OnInitializationFailed(GameSessionState state) =>
      state.Status == SessionStatus.Starting
          ? EndMatch(state, MatchResult.CriticalError(null), stopSimulation: false)
          : SessionTransition.Unchanged(state);

  /// <summary>
  /// Simulation informó victoria, empate o falla crítica. En Running fija el resultado y vacía el lote: un resultado del mundo
  /// prevalece sobre las desconexiones (D-055). En cualquier otro estado ya hay resultado o no hay partida: se ignora.
  /// </summary>
  private static SessionTransition OnSimulationResult(GameSessionState state, MatchResult result) =>
      state.Status == SessionStatus.Running
          ? EndMatch(state, result, stopSimulation: !state.StopRequested)
          : SessionTransition.Unchanged(state);

  /// <summary>
  /// Terminó la detención pedida: ya no corre ningún tick, así que el lote está completo y recién ahora se decide el resultado
  /// (D-055). Sin detención pedida, el aviso no corresponde a nada y se ignora.
  /// </summary>
  private static SessionTransition OnSimulationStopped(GameSessionState state, TickId? lastConfirmedTick) =>
      state.Status == SessionStatus.Running && state.StopRequested
          ? EndMatch(state, ResultFromBatch(state, lastConfirmedTick), stopSimulation: false)
          : SessionTransition.Unchanged(state);

  /// <summary>
  /// Resultado según el lote de desconexiones: un jugador ⇒ gana el otro; los dos ⇒ final técnico; ninguno (la detención
  /// la pidió solo el apagado) ⇒ <c>ServerShutdown</c> (D-084).
  /// </summary>
  private static MatchResult ResultFromBatch(GameSessionState state, TickId? finalTick)
  {
    EquatableArray<PlayerId> lost = state.PendingDisconnects;
    if (lost.IsEmpty)
    {
      return MatchResult.ServerShutdown(finalTick);
    }

    return lost.Count < PlayersPerMatch ? MatchResult.PlayerDisconnected(lost[0].Opponent(), finalTick) : MatchResult.BothPlayersDisconnected(finalTick);
  }

  /// <summary>
  /// Fija el resultado y pasa a Ending. Orden de acciones (07 §3): <c>StopSimulation</c> (si hace falta) → <c>MatchEnded</c>
  /// a cada jugador conectado → <c>ShutdownConnections</c>. <c>MatchEnded</c> sale con los ticks ya detenidos, así queda
  /// detrás del último lote de gameplay (07 §6).
  /// </summary>
  private static SessionTransition EndMatch(GameSessionState state, MatchResult result, bool stopSimulation)
  {
    GameSessionState ending = SessionStateBuilder.From(state).WithStatus(SessionStatus.Ending).WithResult(result).Build();
    var actions = new List<SessionAction>();
    if (stopSimulation)
    {
      actions.Add(new StopSimulation());
    }

    actions.AddRange(ending.OccupiedSlots.Where(slot => slot.IsConnected).Select(slot => Send(slot.Connection, new MatchEnded(ending.Session, result))));
    actions.Add(new ShutdownConnections());
    return new(ending, actions.ToEquatableArray());
  }

  /// <summary>
  /// Pedido de apagado del operador (D-084). Sin partida en marcha no hay nada que decirle al cliente más que el cierre; en
  /// Running primero se detienen los ticks; con el resultado ya fijado no cambia nada.
  /// </summary>
  private static SessionTransition OnShutdownRequested(GameSessionState state) => state.Status switch
  {
    SessionStatus.WaitingForPlayers or SessionStatus.Starting => EndWithoutMatch(state),
    SessionStatus.Running => RequestShutdownInRunning(state),
    _ => SessionTransition.Unchanged(state),
  };

  /// <summary>Waiting o Starting: no hubo partida. El motivo llega a los clientes en <c>Disconnecting(ServerShuttingDown)</c>.</summary>
  private static SessionTransition EndWithoutMatch(GameSessionState state)
  {
    GameSessionState ending = SessionStateBuilder.From(state).WithStatus(SessionStatus.Ending).WithResult(MatchResult.ServerShutdown(null)).Build();
    return new(ending, [new ShutdownConnections()]);
  }

  /// <summary>Running: se pide detener los ticks una sola vez. El resultado se fija en <c>SimulationStopped</c>, si no llegó otro.</summary>
  private static SessionTransition RequestShutdownInRunning(GameSessionState state)
  {
    GameSessionState stopping = SessionStateBuilder.From(state).WithShutdownRequested(true).WithStopRequested(true).Build();
    return state.StopRequested ? WithoutActions(stopping) : new(stopping, [new StopSimulation()]);
  }

  /// <summary>Networking terminó de drenar y cerrar: la sesión pasa a Ended y el host completa su ejecución (D-058).</summary>
  private static SessionTransition OnShutdownCompleted(GameSessionState state)
  {
    if (state.Status != SessionStatus.Ending)
    {
      return SessionTransition.Unchanged(state);
    }

    GameSessionState ended = SessionStateBuilder.From(state).WithStatus(SessionStatus.Ended).Build();
    return new(ended, [new CompleteSession(state.Result!)]);
  }
}
