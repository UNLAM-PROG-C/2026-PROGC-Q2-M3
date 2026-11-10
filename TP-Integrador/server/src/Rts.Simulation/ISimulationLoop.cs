using Rts.Shared.Match;

namespace Rts.Simulation;

/// <summary>
/// Loop temporizado de Simulation. Implementa <see cref="ISimulationControl"/> para GameSession y llama a
/// <see cref="ISimulationEngine.AdvanceTick"/> a 20 Hz desde un único hilo dedicado. Contrato:
/// <list type="bullet">
/// <item>Sin ticks solapados: el próximo tick empieza después de que termina el anterior.</item>
/// <item>DECIDIDO (D-047), sin recuperar ticks: si un tick se pasa del intervalo, se registra la sobrecarga, el próximo arranca de
/// inmediato y el reloj se re-ancla. No hay ráfagas para alcanzar el tiempo perdido.</item>
/// <item>Usa un reloj monotónico.</item>
/// <item>Ante un resultado de gameplay o una falla crítica, avisa por <see cref="IMatchResultSink"/> y deja de ejecutar ticks.</item>
/// <item>Supervisión (06 §7): si un tick queda bloqueado más allá del plazo de seguridad, se informa una falla crítica desde fuera
/// del hilo bloqueado.</item>
/// </list>
/// </summary>
public interface ISimulationLoop : ISimulationControl
{
  /// <summary>Verdadero mientras el loop ejecuta ticks.</summary>
  bool IsRunning { get; }
}
