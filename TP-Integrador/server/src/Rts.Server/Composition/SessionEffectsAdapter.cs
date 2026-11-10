using Rts.GameSession;
using Rts.GameSession.Actions;
using Rts.Networking;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;
using Rts.Shared.World;
using Rts.StateSync;

namespace Rts.Server.Composition;

/// <summary>
/// Implementa el puerto de efectos de GameSession (contrato 07 §1) sobre Networking y State Sync. Solo delega: no agrega reglas.
/// Gracias a este adaptador, GameSession no referencia a ninguno de los dos.
/// </summary>
internal sealed class SessionEffectsAdapter(INetworkServer network, IStateSyncPublisher stateSync) : ISessionEffects
{
  public void Send(ConnectionId connection, ISessionMessage message) => network.Send(connection, message);

  public void Close(ConnectionId connection, ErrorCode reason) => network.Close(connection, reason);

  /// <summary>
  /// State Sync dirige el InitialState por <see cref="PlayerId"/> y Networking resuelve la conexión (D-069). Los destinatarios
  /// llegan para dejar explícito a quién va, pero no hace falta pasar sus conexiones.
  /// </summary>
  public void PublishInitialState(SessionId session, InitializationId initialization, WorldView world, SessionRecipient player1, SessionRecipient player2) =>
      stateSync.PublishInitialState(session, initialization, world);

  public Task DrainAndCloseAllAsync() => network.DrainAndCloseAllAsync();
}
