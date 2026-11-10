using Rts.Shared.Errors;

namespace Rts.Networking.Admission;

/// <summary>
/// Límite de comandos de gameplay por cliente (08 §4). RECOMENDADO (D-077): balde de fichas con capacidad <c>CommandBurst</c> que se
/// recarga a <c>CommandsPerSecond</c>, medido con <see cref="TimeProvider"/>. Uno por conexión; lo usa solo su receptor.
/// Rechazar no quita nada ya admitido.
/// </summary>
public interface ICommandRateLimiter
{
  /// <summary>Consume una ficha si hay; si no, el comando se rechaza con <c>Network.RateLimited</c>.</summary>
  bool TryAcquire();
}

/// <summary>
/// Contador de faltas de una conexión (DECIDIDO D-074). Una falta es un mensaje inválido o un comando rechazado por límite por segundo.
/// No son faltas: <c>MatchNotRunning</c>, <c>CommandQueueFull</c> ni <c>SessionBusy</c>, que no son culpa del cliente.
/// Uno por conexión; lo usa solo su receptor.
/// </summary>
public interface IViolationTracker
{
  /// <summary>Registra una falta. Devuelve <c>true</c> si se superó <c>MaxViolations</c> dentro de <c>ViolationWindow</c>: hay que cerrar con <c>Network.AbuseDetected</c>.</summary>
  bool Register(ErrorCode violation);

  /// <summary>Faltas dentro de la ventana actual (métricas).</summary>
  int RecentCount { get; }
}
