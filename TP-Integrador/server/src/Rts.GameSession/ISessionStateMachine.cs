using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Ids;

namespace Rts.GameSession;

/// <summary>
/// Máquina de estados de la sesión (07 §3-6), como función pura: no hace I/O, no usa relojes ni hilos, y para el mismo estado
/// y el mismo evento devuelve siempre la misma transición. Se prueba con secuencias de eventos, sin sockets (07 §10).
/// La tabla completa de transiciones está en el contrato 07 §3.
/// </summary>
public interface ISessionStateMachine
{
  /// <summary>Estado inicial: WaitingForPlayers, sin plazas.</summary>
  GameSessionState CreateInitial(SessionId session);

  /// <summary>
  /// Aplica un evento. Nunca lanza por eventos inesperados (tardíos, duplicados o de conexiones desconocidas): los ignora o
  /// responde con <c>SessionRequestRejected</c>. Lanza solo si recibe un estado o evento nulo.
  /// </summary>
  SessionTransition Apply(GameSessionState state, SessionEvent sessionEvent);
}
