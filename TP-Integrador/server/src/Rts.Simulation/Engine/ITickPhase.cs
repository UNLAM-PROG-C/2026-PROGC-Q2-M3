using Rts.Simulation.Diagnostics;

namespace Rts.Simulation.Engine;

/// <summary>
/// Una fase del tick (contrato 06 §4). El motor las ejecuta en orden sobre el mismo <see cref="TickContext"/>.
/// Una fase lee <c>context.World</c>, llama a su módulo de dominio, confirma el resultado reemplazando <c>context.World</c> y
/// sella sus eventos con <c>context.Emit</c>. Si no puede terminar de forma consistente, lanza una excepción: el motor
/// descarta el tick completo.
/// </summary>
internal interface ITickPhase
{
  /// <summary>Fase que representa, para las métricas y para informar dónde ocurrió una falla.</summary>
  SimulationPhase Phase { get; }

  void Execute(TickContext context);
}
