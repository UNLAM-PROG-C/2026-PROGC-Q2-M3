using Rts.Production.Contracts;

namespace Rts.Production;

/// <summary>
/// Cálculo de la fase 8 (05 §7, <c>CalculateTick</c>): progreso, apariciones e inicios. Contrato:
/// <list type="bullet">
/// <item>Función pura y determinista; no muta la entrada ni hace I/O.</item>
/// <item>Aplica las reglas del contrato 05 §4, en este orden: progreso → apariciones → inicios.</item>
/// <item>Nunca inicia una tarea en un productor que estaba ocupado al entrar a la fase, aunque su unidad aparezca en ella (05 §4).</item>
/// <item>Nunca propone dos apariciones en la misma celda (05 §5).</item>
/// </list>
/// </summary>
public interface IProductionCalculator
{
    ProductionTickResult CalculateTick(ProductionTickInput input);
}
