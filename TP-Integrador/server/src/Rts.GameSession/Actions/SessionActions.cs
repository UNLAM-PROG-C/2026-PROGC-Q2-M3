using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession.Actions;

/// <summary>
/// Acción propuesta por una transición (07 §8, §12). La máquina de estados no hace I/O: el host ejecuta las acciones
/// <b>en el orden en que vienen</b>, y ese orden es parte del contrato (por ejemplo, <c>MatchStarted</c> antes de
/// <see cref="StartSimulation"/>).
/// </summary>
public abstract record SessionAction;

/// <summary>Encolar un mensaje de sesión en la cola de salida de una conexión. No bloquea.</summary>
public sealed record SendSessionMessage(ConnectionId Connection, ISessionMessage Message) : SessionAction;

/// <summary>Cerrar una conexión: Networking envía un aviso final con el motivo, drena brevemente y cierra (D-059).</summary>
public sealed record CloseConnection(ConnectionId Connection, ErrorCode Reason) : SessionAction;

/// <summary>Destinatario de un InitialState.</summary>
public sealed record SessionRecipient(PlayerId Player, ConnectionId Connection);

/// <summary>
/// Pedir a Simulation el mundo inicial (<see cref="ISimulationControl.InitializeMatch"/>) y a State Sync un InitialState por
/// destinatario con esta inicialización (07 §3 paso 3). Si falla, el host informa <see cref="Events.InitializationFailed"/>.
/// </summary>
public sealed record PrepareInitialState(InitializationId Initialization, SessionRecipient Player1, SessionRecipient Player2) : SessionAction;

/// <summary>Programar <see cref="Events.ReadyTimeoutElapsed"/> para esa inicialización, con reloj monotónico (07 §4).</summary>
public sealed record ScheduleReadyTimeout(InitializationId Initialization, TimeSpan Delay) : SessionAction;

/// <summary>Arrancar el loop de ticks (<see cref="ISimulationControl.StartTicks"/>).</summary>
public sealed record StartSimulation : SessionAction;

/// <summary>
/// Detener el loop (<see cref="ISimulationControl.StopTicksAsync"/>) sin bloquear al consumidor; al terminar, el host informa
/// <see cref="Events.SimulationStopped"/> (D-055).
/// </summary>
public sealed record StopSimulation : SessionAction;

/// <summary>Pedir a Networking drenar las colas de salida y cerrar todas las conexiones; al terminar, <see cref="Events.ShutdownCompleted"/>.</summary>
public sealed record ShutdownConnections : SessionAction;

/// <summary>Sesión terminada (Ended): el host completa su ejecución con este resultado y el proceso termina (D-058).</summary>
public sealed record CompleteSession(MatchResult Result) : SessionAction;
