using Rts.GameSession.Actions;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;
using Rts.Shared.World;

namespace Rts.GameSession;

/// <summary>
/// Efectos externos que necesita el host, sin que GameSession referencie Networking ni State Sync (D-059). Lo implementa la
/// composición del servidor (<c>Rts.Server</c>). Todos los métodos sincrónicos solo encolan: nunca escriben en un socket.
/// Para una misma conexión, el orden de llamada es el orden de envío (08 §5).
/// </summary>
public interface ISessionEffects
{
    /// <summary>Encola un mensaje de sesión para la conexión.</summary>
    void Send(ConnectionId connection, ISessionMessage message);

    /// <summary>Encola un aviso final con el motivo y cierra la conexión después de un drenaje breve.</summary>
    void Close(ConnectionId connection, ErrorCode reason);

    /// <summary>State Sync arma y encola un InitialState por destinatario con esa sesión e inicialización (09 §3, contrato 09 §4).</summary>
    void PublishInitialState(SessionId session, InitializationId initialization, WorldView world, SessionRecipient player1, SessionRecipient player2);

    /// <summary>Drena las colas de salida con el plazo de Networking y cierra todo. Termina aunque haya sockets muertos.</summary>
    Task DrainAndCloseAllAsync();
}
