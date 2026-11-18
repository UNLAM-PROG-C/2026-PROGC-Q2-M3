using Microsoft.Extensions.Logging;
using Rts.GameSession.Actions;
using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession;

/// <summary>
/// Los mensajes de log del host de sesión (D-096). Son texto sobre lo que la máquina de estados <b>ya decidió</b>: cambios de
/// estado, plazas, cierres y el resultado oficial. No agregan reglas ni cambian ninguna decisión; el que escribe es el Server.
/// </summary>
internal static class SessionHostLog
{
  private const string Nobody = "ninguno";

  /// <summary>Lo que cambió con un evento: el estado, el pedido de detener los ticks y el resultado oficial, que se fija una sola vez.</summary>
  public static void Transition(ILogger logger, GameSessionState previous, SessionEvent sessionEvent, GameSessionState current)
  {
    string cause = sessionEvent.GetType().Name;
    if (current.Status != previous.Status)
    {
      logger.LogInformation("Sesión: {From} → {To} por {Event}", previous.Status, current.Status, cause);
    }

    StopRequest(logger, previous, current, cause);
    if (previous.Result is null && current.Result is { } result)
    {
      Result(logger, result);
    }
  }

  private static void StopRequest(ILogger logger, GameSessionState previous, GameSessionState current, string cause)
  {
    if (current.StopRequested && !previous.StopRequested)
    {
      logger.LogInformation("Sesión: se pidió detener los ticks por {Event}; ya no se acepta gameplay", cause);
    }
  }

  /// <summary>Las acciones que conviene ver en el log: quién es quién, intentos cancelados, solicitudes sin efecto y cierres.</summary>
  public static void Action(ILogger logger, SessionAction action)
  {
    if (action is CloseConnection close)
    {
      logger.LogInformation("Sesión: se cierra la conexión {ConnectionId} ({Reason})", close.Connection, close.Reason);
    }
    else if (action is SendSessionMessage send)
    {
      Message(logger, send);
    }
  }

  private static void Message(ILogger logger, SendSessionMessage send)
  {
    switch (send.Message)
    {
      case PlayerAssigned assigned:
        logger.LogInformation("Sesión: la conexión {ConnectionId} es el jugador {Player}", send.Connection, assigned.Player);
        break;
      case StartAborted aborted:
        logger.LogInformation("Sesión: inicio {Initialization} cancelado ({Reason}); la conexión {ConnectionId} vuelve a esperar", aborted.Initialization, aborted.Reason, send.Connection);
        break;
      case SessionRequestRejected rejected:
        logger.LogDebug("Sesión: solicitud de la conexión {ConnectionId} sin efecto ({Reason})", send.Connection, rejected.Reason);
        break;
    }
  }

  private static void Result(ILogger logger, MatchResult result) => logger.LogInformation(
      "Sesión: resultado oficial {ResultType} ({Reason}); ganador: {Winner}; último tick: {FinalTick}",
      result.ResultType,
      result.Reason,
      result.Winner?.ToString() ?? Nobody,
      result.FinalTick?.ToString() ?? Nobody);
}
