using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Match;

namespace Rts.Shared.Session;

/// <summary>
/// Mensaje de sesión que GameSession dirige a una conexión (07 §9). Es un modelo de integración, no un DTO JSON:
/// Networking lo traduce a su DTO y lo encola en la cola de salida de esa conexión, respetando el orden de emisión.
/// <c>InitialState</c> no está acá: lo arma State Sync.
/// </summary>
public interface ISessionMessage
{
}

/// <summary>Plaza asignada a la conexión (07 §9). Se envía apenas se registra, antes de cualquier InitialState.</summary>
public sealed record PlayerAssigned(SessionId Session, PlayerId Player) : ISessionMessage;

/// <summary>Motivo de cancelación de un intento de inicio (DECIDIDO D-057).</summary>
public enum StartAbortReason
{
  /// <summary>El rival se desconectó durante Starting.</summary>
  OpponentLeft = 1,

  /// <summary>Venció <c>ReadyTimeout</c> y el rival no confirmó.</summary>
  ReadyTimeout = 2,
}

/// <summary>
/// DECIDIDO (D-057): el intento de inicio <see cref="Initialization"/> quedó cancelado. El cliente descarta el mundo cargado
/// y vuelve a esperar rival; sus Ready para esa inicialización ya no valen.
/// </summary>
public sealed record StartAborted(InitializationId Initialization, StartAbortReason Reason) : ISessionMessage;

/// <summary>Inicio oficial (07 §3): después de ambos Ready válidos y antes de cualquier publicación de gameplay.</summary>
public sealed record MatchStarted(SessionId Session, InitializationId Initialization) : ISessionMessage;

/// <summary>Fin oficial (07 §9): después del último lote de gameplay. Se envía a cada jugador que siga conectado.</summary>
public sealed record MatchEnded(SessionId Session, MatchResult Result) : ISessionMessage;

/// <summary>
/// Una solicitud de sesión del cliente no tuvo efecto (por ejemplo, un <c>ClientReady</c> viejo o fuera de Starting).
/// No cierra la conexión.
/// </summary>
public sealed record SessionRequestRejected(ErrorCode Reason, InitializationId? Initialization) : ISessionMessage;
