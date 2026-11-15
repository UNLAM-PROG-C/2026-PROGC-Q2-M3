namespace Rts.Simulation.Diagnostics;

/// <summary>
/// Observador del diagnóstico de Simulation (DECIDIDO D-095). Simulation no loguea: le avisa <b>datos</b> a esta interfaz, y quien
/// la implementa decide qué hacer con ellos (el Server los escribe en el log; una medición los acumula; un test los guarda).
/// <para>
/// <b>Contrato para quien la implemente:</b> la llaman hilos distintos (el del tick y, desde el paso de fallas críticas, el
/// supervisor), así que tiene que tolerar llamadas concurrentes; y <b>no debe bloquear</b>, porque corre en el camino del tick. Si
/// lanza una excepción, el loop la ignora: el diagnóstico no es parte del juego y no termina la partida.
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
}
