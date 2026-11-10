using Rts.Combat.Contracts;

namespace Rts.Combat;

/// <summary>
/// Cálculo de combate de un tick (02 §6, <c>CalculateTick</c>). Lo invoca Simulation en la fase 5, en paralelo con
/// Economy y sobre el mismo snapshot. Contrato:
/// <list type="bullet">
/// <item>Función pura: no muta la entrada ni estado compartido, ni realiza I/O. Es seguro invocarla desde cualquier hilo.</item>
/// <item>Determinista: igual entrada ⇒ resultado igual, sin importar el orden de las colecciones ni el orden de finalización
/// de sus subcálculos internos (soldados ∥ torres).</item>
/// <item>Aplica las reglas del contrato 02 §4 (objetivos, rangos, cooldowns, daño agregado, destrucciones).</item>
/// </list>
/// </summary>
public interface ICombatCalculator
{
  CombatTickResult CalculateTick(CombatTickInput input);
}
