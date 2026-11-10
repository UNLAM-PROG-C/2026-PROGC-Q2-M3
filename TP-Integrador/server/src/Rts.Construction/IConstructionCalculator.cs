using Rts.Construction.Contracts;

namespace Rts.Construction;

/// <summary>
/// Cálculo de la fase 3 (03 §6: <c>CalculateBuildProgress</c> + <c>CalculateRepairProgress</c>). Contrato:
/// <list type="bullet">
/// <item>Función pura y determinista; no muta la entrada ni hace I/O.</item>
/// <item>Los aportes de varios trabajadores se suman como contribuciones agregadas, no como escritores concurrentes (03 §7).</item>
/// <item>Aplica las reglas del contrato 03 §5.</item>
/// </list>
/// </summary>
public interface IConstructionCalculator
{
    ConstructionTickResult CalculateTick(ConstructionTickInput input);
}
