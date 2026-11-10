using Rts.Shared.Ids;
using Rts.Shared.Match;

namespace Rts.GameSession.Events;

/// <summary>
/// Evento de sesión ya serializado (07 §7). Llegan desde los receptores de Networking, el timer de Ready, Simulation y el propio
/// host, pero el estado solo cambia en el consumidor único, de a uno por vez.
/// </summary>
public abstract record SessionEvent;

/// <summary>Hello con versión compatible: la conexión pide plaza (D-053).</summary>
public sealed record ClientHelloReceived(ConnectionId Connection) : SessionEvent;

/// <summary><c>ClientReady</c> recibido para una inicialización.</summary>
public sealed record ClientReadyReceived(ConnectionId Connection, InitializationId Initialization) : SessionEvent;

/// <summary>La conexión se cerró o se perdió. Nunca se descarta.</summary>
public sealed record ConnectionClosed(ConnectionId Connection) : SessionEvent;

/// <summary>Venció el plazo de Ready de esa inicialización. Si ya no es la vigente, se ignora (07 §4).</summary>
public sealed record ReadyTimeoutElapsed(InitializationId Initialization) : SessionEvent;

/// <summary>Simulation informó victoria, empate o falla crítica por <see cref="IMatchResultSink"/>.</summary>
public sealed record SimulationResultReceived(MatchResult Result) : SessionEvent;

/// <summary>
/// Terminó el <c>StopTicksAsync</c> pedido por GameSession: ya no puede correr ningún tick. Cierra el lote de desconexiones (D-055).
/// </summary>
/// <param name="LastConfirmedTick">Último tick confirmado, o <c>null</c> si la detención falló y no se conoce.</param>
public sealed record SimulationStopped(TickId? LastConfirmedTick) : SessionEvent;

/// <summary><c>InitializeMatch</c> o la publicación del InitialState fallaron: no se puede preparar la partida (D-060).</summary>
public sealed record InitializationFailed(string Reason) : SessionEvent;

/// <summary>Networking terminó de drenar y cerrar todas las conexiones (o se agotó su plazo de drenaje).</summary>
public sealed record ShutdownCompleted : SessionEvent;

/// <summary>
/// El operador pidió apagar el servidor (Ctrl+C o señal del sistema, D-084). Si no hay otro resultado, la partida termina con
/// <see cref="MatchEndReason.ServerShutdown"/>; en Running, primero se detienen los ticks.
/// </summary>
public sealed record ShutdownRequested : SessionEvent;
