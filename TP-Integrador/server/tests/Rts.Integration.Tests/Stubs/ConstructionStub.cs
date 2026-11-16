using Rts.Construction;
using Rts.Construction.Configuration;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Integration.Tests.Stubs;

/// <summary>
/// Reemplazo provisorio de Construction, que todavía no está implementado (es de Ivan). Responde solo lo que Simulation necesita
/// aunque nadie construya: la huella y la vida de cada edificio, con los valores reales de <see cref="ConstructionConfig"/> (las usa
/// el mundo inicial para el Centro Principal), y un tick sin obras. Todo lo demás lanza: si un escenario lo toca por error, la prueba
/// falla a la vista. Se borra cuando exista el módulo real.
/// </summary>
internal sealed class ConstructionStub : IConstructionRules, IConstructionCalculator
{
  private readonly ConstructionConfig _config;

  public ConstructionStub(ConstructionConfig config)
  {
    _config = config ?? throw new ArgumentNullException(nameof(config));
  }

  public FootprintSize GetFootprintSize(BuildingType buildingType) => _config.FootprintSizes[buildingType];

  public int GetMaxHp(BuildingType buildingType) => _config.BuildingMaxHp[buildingType];

  public ConstructionTickResult CalculateTick(ConstructionTickInput input) => ConstructionTickResult.Empty(input.Tick);

  public int GetInitialHp(BuildingType buildingType) => throw NotImplementedYet();

  public SiteEvaluation EvaluateSite(BuildingType buildingType, GridCell anchor, OccupancyView site) => throw NotImplementedYet();

  public PlacementEvaluation EvaluatePlacement(PlacementInput input) => throw NotImplementedYet();

  public ErrorCode? EvaluateContinueBuild(PlayerId requester, ConstructionTargetInfo? target) => throw NotImplementedYet();

  public RepairStartEvaluation EvaluateRepairStart(PlayerId requester, RepairTargetInfo? target) => throw NotImplementedYet();

  private static NotSupportedException NotImplementedYet() =>
      new("Construction todavía no está implementado: ningún escenario de integración debería llegar acá.");
}
