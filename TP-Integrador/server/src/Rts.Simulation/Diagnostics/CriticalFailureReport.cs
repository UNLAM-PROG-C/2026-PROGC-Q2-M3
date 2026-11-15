using Rts.Shared.Ids;

namespace Rts.Simulation.Diagnostics;

/// <summary>Cómo se detectó una falla crítica del loop (06 §7, DECIDIDO D-099). Cada una termina la partida en <c>CriticalError</c>.</summary>
public enum CriticalFailureKind
{
  /// <summary>Una fase del tick lanzó (incluye la barrera vencida y un resultado incoherente de un módulo): el tick se descartó.</summary>
  PhaseFailed = 1,

  /// <summary>El motor lanzó fuera de sus fases: un error del propio motor, no de un módulo.</summary>
  UnexpectedException = 2,

  /// <summary>State Sync lanzó al publicar un tick ya confirmado: los clientes quedarían desincronizados.</summary>
  PublisherFailed = 3,

  /// <summary>El supervisor vio un tick en curso por más de <c>TickStallTimeout</c> y lo abandonó (06 §6, D-089).</summary>
  TickStalled = 4,
}

/// <summary>
/// Reporte de una falla crítica para <see cref="ISimulationDiagnostics.OnCriticalFailure"/> (DECIDIDO D-099). Son datos, no texto
/// (D-095): el adaptador del Server arma el mensaje según <see cref="Kind"/> y escribe la traza de <see cref="Exception"/>.
/// </summary>
/// <param name="Kind">Cómo se detectó.</param>
/// <param name="Tick">El tick en que se detectó: el que estaba en curso, o el que no se pudo publicar.</param>
/// <param name="LastConfirmedTick">El último tick confirmado: el <c>FinalTick</c> del <c>CriticalError</c> que recibe GameSession.</param>
/// <param name="Phase">La fase que falló, solo en <see cref="CriticalFailureKind.PhaseFailed"/>.</param>
/// <param name="Reason">Motivo legible.</param>
/// <param name="Exception">La excepción original, si la hubo (un tick bloqueado no tiene).</param>
public sealed record CriticalFailureReport(
    CriticalFailureKind Kind,
    TickId Tick,
    TickId LastConfirmedTick,
    SimulationPhase? Phase,
    string Reason,
    Exception? Exception);
