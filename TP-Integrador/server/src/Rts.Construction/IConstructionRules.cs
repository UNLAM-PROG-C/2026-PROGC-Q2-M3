using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Construction;

/// <summary>
/// Reglas de construcción que Simulation consulta en la fase 1 y al inicializar la partida.
/// Implementaciones puras y seguras para uso concurrente. Ningún método cobra ni muta estado.
/// </summary>
public interface IConstructionRules
{
  /// <summary>Tamaño de huella del tipo (todos los tipos, incluido el Centro Principal; valla 1×1).</summary>
  FootprintSize GetFootprintSize(BuildingType buildingType);

  /// <summary>HP máximo del tipo (D-021); también es la meta del progreso de obra.</summary>
  int GetMaxHp(BuildingType buildingType);

  /// <summary>HP con el que nace una obra: <c>ceil(MaxHp × InitialHpFraction)</c>, en [1, MaxHp − 1] (D-035).</summary>
  int GetInitialHp(BuildingType buildingType);

  /// <summary>
  /// Valida el sitio de una colocación (tipo construible, límites, estructuras, árboles, unidades presentes), sin
  /// trabajadores ni costo. Si es válido, devuelve la huella para que Simulation arme el mapa hipotético de Movement.
  /// </summary>
  SiteEvaluation EvaluateSite(BuildingType buildingType, GridCell anchor, OccupancyView site);

  /// <summary>
  /// Evaluación completa de la forma "colocar" de Build (03 §2): sitio + al menos un trabajador alcanzable.
  /// Acepta parcialmente el grupo; los trabajadores rechazados conservan su orden anterior.
  /// </summary>
  PlacementEvaluation EvaluatePlacement(PlacementInput input);

  /// <summary>
  /// Valida la forma "continuar" de Build (D-032): el objetivo existe (<paramref name="target"/> no nulo), es del
  /// jugador y sigue en obra. Devuelve <c>null</c> si es válido.
  /// </summary>
  ErrorCode? EvaluateContinueBuild(PlayerId requester, ConstructionTargetInfo? target);

  /// <summary>
  /// Valida <c>RepairFarm</c> (03 §4): granja completada, propia y agotada. Si ya hay reparación en curso, los
  /// trabajadores se suman sin pagar (<see cref="RepairStartKind.Join"/>).
  /// </summary>
  RepairStartEvaluation EvaluateRepairStart(PlayerId requester, RepairTargetInfo? target);
}
