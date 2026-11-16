using Rts.GameSession;
using Rts.Networking;
using Rts.Simulation.Commands;
using Rts.StateSync;

namespace Rts.Server.Composition;

/// <summary>
/// Pasos 1 a 4 del orden de construcción (contrato 10 §4): las piezas pasivas, que solo reciben, y las de transporte, que llevan
/// comandos hacia Simulation y mensajes hacia los clientes. Ninguna arranca hilos ni abre el puerto al crearse: eso pasa recién
/// cuando la composición llama a <c>Network.RunAsync</c>.
/// </summary>
/// <param name="Commands">Cola global: <c>ICommandSink</c> para Networking y <c>ICommandSource</c> para Simulation (paso 1).</param>
/// <param name="Inbox">Buzón de sesión: <c>ISessionEventSink</c> para Networking e <c>IMatchResultSink</c> para Simulation (paso 2).</param>
/// <param name="SessionView">Vista de sesión: <c>ISessionGate</c> para Networking; la escribe solo el host de sesión (paso 2).</param>
/// <param name="Network">Servidor TCP: recibe, admite y escribe; es el <c>IClientOutbox</c> de State Sync (paso 3).</param>
/// <param name="StateSync">Publicador: el <c>ITickPublisher</c> de Simulation (paso 4).</param>
internal sealed record TransportPieces(
    CommandQueue Commands,
    SessionInbox Inbox,
    SessionStateStore SessionView,
    NetworkServer Network,
    StateSyncPublisher StateSync);
