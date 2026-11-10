using Rts.Shared.Ids;
using Rts.Shared.World;

namespace Rts.StateSync;

/// <summary>
/// Publicador de State Sync (DECIDIDO D-064): proyecta con <see cref="Projection.IStateProjector"/> y encola el resultado en
/// <c>IClientOutbox</c> <b>dentro de la misma llamada</b>, sin hilos ni colas propias. Al volver, los mensajes ya están en las
/// colas de salida en orden; por eso un <c>MatchEnded</c> encolado después de detener los ticks queda siempre detrás del último lote.
/// <list type="bullet">
/// <item><see cref="ITickPublisher.Publish"/> se llama desde el hilo de Simulation, en la fase 9. No bloquea ni hace I/O: solo arma
/// DTOs y encola. La serialización a bytes ocurre en el escritor de cada conexión.</item>
/// <item><see cref="PublishInitialState"/> se llama desde el host de GameSession durante Starting. Nunca coincide con
/// <c>Publish</c>, porque en Starting no corren ticks.</item>
/// </list>
/// </summary>
public interface IStateSyncPublisher : ITickPublisher
{
  /// <summary>Arma y encola un InitialState para P1 y otro para P2 con esa sesión e inicialización.</summary>
  void PublishInitialState(SessionId session, InitializationId initialization, WorldView world);
}
