using Rts.Shared.Errors;

namespace Rts.Simulation;

/// <summary>Códigos de error propios de Simulation (D-007, prefijo <c>Simulation.</c>).</summary>
public static class SimulationErrors
{
  /// <summary>
  /// El tipo de comando todavía no tiene handler en la fase 1. Es temporal: existe mientras se implementan los comandos de
  /// a uno (la vertical slice solo procesa <c>Move</c> y <c>Stop</c>).
  /// </summary>
  public static readonly ErrorCode UnsupportedCommand = new("Simulation.UnsupportedCommand");
}
