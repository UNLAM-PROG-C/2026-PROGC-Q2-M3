namespace Rts.Simulation.Diagnostics;

/// <summary>
/// Observador del diagnóstico de Simulation (DECIDIDO D-095). Simulation no loguea: le avisa <b>datos</b> a esta interfaz, y quien
/// la implementa decide qué hacer con ellos (el Server los escribe en el log; una medición los acumula; un test los guarda).
/// <para>
/// <b>Contrato para quien la implemente:</b> la llaman hilos distintos (el del tick y el supervisor de ticks bloqueados), así que
/// tiene que tolerar llamadas concurrentes; y <b>no debe bloquear</b>, porque corre en el camino del tick. Si lanza una excepción,
/// el loop la ignora: el diagnóstico no es parte del juego y no termina la partida.
/// </para>
/// Después de que <c>StopTicksAsync</c> se completa, no recibe más llamadas.
/// </summary>
public interface ISimulationDiagnostics
{
  /// <summary>
  /// Un tick que el motor terminó, confirmado o descartado por una falla en una fase, con <see cref="TickMetrics.IsOverrun"/> ya
  /// decidido por la agenda del loop. Llega en orden de tick, una vez por tick.
  /// </summary>
  void OnTickCompleted(TickMetrics metrics);

  /// <summary>
  /// La falla crítica que terminó la partida (D-099): <b>una sola vez</b> por partida y <b>antes</b> de avisarle el
  /// <c>CriticalError</c> a GameSession, así el motivo queda en el log antes del cierre. Si la falla fue en una fase, las métricas
  /// de ese tick llegan por <see cref="OnTickCompleted"/> justo después.
  /// </summary>
  void OnCriticalFailure(CriticalFailureReport report);
}

/// <summary>Observador que no hace nada: para pruebas o para armar Simulation sin diagnóstico.</summary>
public sealed class NullSimulationDiagnostics : ISimulationDiagnostics
{
  public static readonly NullSimulationDiagnostics Instance = new();

  private NullSimulationDiagnostics()
  {
  }

  public void OnTickCompleted(TickMetrics metrics)
  {
  }

  public void OnCriticalFailure(CriticalFailureReport report)
  {
  }
}
