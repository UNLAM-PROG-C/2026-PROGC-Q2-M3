using Rts.Shared.Errors;

namespace Rts.GameSession;

/// <summary>Códigos de error del área Session (D-007). Ninguno termina la partida por sí mismo.</summary>
public static class SessionErrors
{
  /// <summary>No hay plaza libre o la partida ya pasó de la espera (07 §2). Motivo de cierre de un tercer cliente.</summary>
  public static readonly ErrorCode MatchFull = new("Session.MatchFull");

  /// <summary>La conexión no tiene plaza asignada (por ejemplo, un <c>ClientReady</c> antes del <c>Hello</c>).</summary>
  public static readonly ErrorCode NotRegistered = new("Session.NotRegistered");

  /// <summary>La conexión ya tiene plaza y envió otro <c>Hello</c>.</summary>
  public static readonly ErrorCode AlreadyRegistered = new("Session.AlreadyRegistered");

  /// <summary><c>ClientReady</c> fuera de <c>Starting</c>.</summary>
  public static readonly ErrorCode NotStarting = new("Session.NotStarting");

  /// <summary><c>ClientReady</c> con un <c>InitializationId</c> que no es el vigente (07 §4).</summary>
  public static readonly ErrorCode StaleInitialization = new("Session.StaleInitialization");

  /// <summary>Motivo de cierre del cliente que no confirmó a tiempo (DECIDIDO D-056).</summary>
  public static readonly ErrorCode ReadyTimeoutExpired = new("Session.ReadyTimeoutExpired");
}
