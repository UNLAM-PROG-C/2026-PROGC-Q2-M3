using Rts.Economy.Contracts;

namespace Rts.Economy;

/// <summary>
/// Cálculo económico de un tick (01 §6, <c>CalculateTick</c>). Lo invoca Simulation en la fase 5, en paralelo con Combat
/// y sobre el mismo snapshot. Contrato:
/// <list type="bullet">
/// <item>Función pura: no muta la entrada ni estado compartido, ni realiza I/O. Es seguro invocarla desde cualquier hilo.</item>
/// <item>Determinista: igual entrada ⇒ resultado igual, sin importar el orden de las colecciones de entrada ni el orden
/// de finalización de sus subcálculos internos (Gathering ∥ FarmProduction).</item>
/// <item>Aplica las reglas del contrato 01 §4 (intervalos, reparto por WorkerId, agotamiento, regeneración, tandas).</item>
/// </list>
/// </summary>
public interface IEconomyCalculator
{
  EconomyTickResult CalculateTick(EconomyTickInput input);
}
