using Rts.Combat;
using Rts.Combat.Model;
using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Economy;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;

namespace Rts.Simulation.Tests.Fakes;

// Fakes de las reglas de los módulos de dominio, para probar Simulation sin sus implementaciones reales.
// Los valores son SUPOSICIONES DE PRUEBA, no balance. Los métodos que todavía no usa ninguna prueba lanzan
// NotSupportedException: se completan cuando la pieza de Simulation que los necesita se implemente.

internal sealed class FakeEconomyRules : IEconomyRules
{
  public static readonly ResourceAmount TestInitialBalance = new(Wood: 200, Food: 100);
  public static readonly ResourceAmount WorkerCost = ResourceAmount.OfFood(3);
  public static readonly ResourceAmount SoldierCost = ResourceAmount.OfFood(5);

  public ResourceAmount InitialBalance => TestInitialBalance;

  public bool TryGetBuildingCost(BuildingType buildingType, out ResourceAmount cost) => throw NotYet();

  public static readonly ResourceAmount FarmRepairCost = ResourceAmount.OfWood(30);

  public ResourceAmount GetFarmRepairCost() => FarmRepairCost;

  public ResourceAmount GetTrainingCost(UnitType unitType) => unitType == UnitType.Worker ? WorkerCost : SoldierCost;

  /// <summary>Regla simple de prueba: alcanza si ninguna componente del saldo queda negativa.</summary>
  public SpendEvaluation EvaluateSpend(ResourceAmount balance, ResourceAmount cost)
  {
    ResourceAmount after = balance - cost;
    return after.IsNonNegative ? SpendEvaluation.Affordable(after) : SpendEvaluation.Insufficient(balance, EconomyErrors.InsufficientResources);
  }

  public bool CanAfford(ResourceAmount balance, ResourceAmount cost) => throw NotYet();

  private readonly List<GatherTargetInfo?> _gatherValidations = [];

  /// <summary>Si es verdadero, <see cref="EvaluateGatherTarget"/> acepta cualquier objetivo, aunque no exista (Economy con errores).</summary>
  public bool AcceptEveryGatherTarget { get; init; }

  public IReadOnlyList<GatherTargetInfo?> GatherValidations => _gatherValidations;

  /// <summary>Sigue la tabla de D-092, en su orden.</summary>
  public ErrorCode? EvaluateGatherTarget(GatherTargetInfo? target)
  {
    _gatherValidations.Add(target);
    if (AcceptEveryGatherTarget)
    {
      return null;
    }

    return target switch
    {
      null => CommonErrors.EntityNotFound,
      { Kind: not EntityKind.Tree } => EconomyErrors.InvalidGatherTarget,
      { TreeStatus: TreeStatus.Depleted } => EconomyErrors.TreeDepleted,
      _ => null,
    };
  }

  private static NotSupportedException NotYet() => new("Todavía no lo usa ninguna prueba de Simulation.");
}

internal sealed class FakeConstructionRules : IConstructionRules
{
  public static readonly FootprintSize MainCenterSize = new(4, 4);
  public const int MainCenterMaxHp = 1000;
  public const int FarmMaxHp = 400;
  public const int BarracksMaxHp = 600;
  public const int TowerMaxHp = 500;
  public const int FenceMaxHp = 50;

  public FootprintSize GetFootprintSize(BuildingType buildingType) =>
      buildingType == BuildingType.MainCenter ? MainCenterSize : throw NotYet();

  public int GetMaxHp(BuildingType buildingType) => buildingType switch
  {
    BuildingType.MainCenter => MainCenterMaxHp,
    BuildingType.Farm => FarmMaxHp,
    BuildingType.Barracks => BarracksMaxHp,
    BuildingType.Tower => TowerMaxHp,
    BuildingType.Fence => FenceMaxHp,
    _ => throw NotYet(),
  };

  public int GetInitialHp(BuildingType buildingType) => throw NotYet();

  public SiteEvaluation EvaluateSite(BuildingType buildingType, GridCell anchor, OccupancyView site) => throw NotYet();

  public PlacementEvaluation EvaluatePlacement(PlacementInput input) => throw NotYet();

  private readonly List<ContinueBuildCall> _continueBuilds = [];

  /// <summary>Si es verdadero, <see cref="EvaluateContinueBuild"/> admite cualquier objetivo (simula un Construction con errores).</summary>
  public bool AcceptEveryContinue { get; init; }

  public IReadOnlyList<ContinueBuildCall> ContinueBuilds => _continueBuilds;

  /// <summary>Sigue la tabla de 03 §3 (forma "continuar"), en su orden.</summary>
  public ErrorCode? EvaluateContinueBuild(PlayerId requester, ConstructionTargetInfo? target)
  {
    _continueBuilds.Add(new ContinueBuildCall(requester, target));
    if (AcceptEveryContinue)
    {
      return null;
    }

    return target switch
    {
      null => CommonErrors.EntityNotFound,
      { Owner: var owner } when owner != requester => CommonErrors.NotOwner,
      { IsCompleted: true } => ConstructionErrors.AlreadyCompleted,
      _ => null,
    };
  }

  private readonly List<RepairStartCall> _repairStarts = [];

  /// <summary>Si no es nulo, <see cref="EvaluateRepairStart"/> responde esto siempre (simula un Construction con errores).</summary>
  public RepairStartEvaluation? ForcedRepairStart { get; init; }

  public IReadOnlyList<RepairStartCall> RepairStarts => _repairStarts;

  /// <summary>Sigue la tabla de 03 §4, en su orden.</summary>
  public RepairStartEvaluation EvaluateRepairStart(PlayerId requester, RepairTargetInfo? target)
  {
    _repairStarts.Add(new RepairStartCall(requester, target));
    if (ForcedRepairStart is { } forced)
    {
      return forced;
    }

    return target switch
    {
      null => RepairStartEvaluation.Invalid(CommonErrors.EntityNotFound),
      { Owner: var owner } when owner != requester => RepairStartEvaluation.Invalid(CommonErrors.NotOwner),
      { BuildingType: not BuildingType.Farm } or { IsCompleted: false } => RepairStartEvaluation.Invalid(ConstructionErrors.NotAFarm),
      { IsDepleted: false } => RepairStartEvaluation.Invalid(ConstructionErrors.FarmNotDepleted),
      { IsRepairInProgress: true } => RepairStartEvaluation.Valid(RepairStartKind.Join),
      _ => RepairStartEvaluation.Valid(RepairStartKind.StartAndPay),
    };
  }

  private static NotSupportedException NotYet() => new("Todavía no lo usa ninguna prueba de Simulation.");
}

/// <summary>
/// <see cref="ValidateAttackTarget"/> sigue la tabla de 02 §6, en su orden. <see cref="AcceptEverything"/> simula un Combat con errores,
/// para probar que Simulation detecta una respuesta incoherente. Guarda los pedidos para revisar qué le pasó Simulation.
/// </summary>
internal sealed class FakeCombatRules : ICombatRules
{
  public const int WorkerMaxHp = 40;
  public const int SoldierMaxHp = 80;

  private readonly List<AttackValidationCall> _attackValidations = [];

  /// <summary>Si es verdadero, acepta cualquier objetivo, aunque no exista.</summary>
  public bool AcceptEverything { get; init; }

  public IReadOnlyList<AttackValidationCall> AttackValidations => _attackValidations;

  public int GetUnitMaxHp(UnitType unitType) => unitType == UnitType.Worker ? WorkerMaxHp : SoldierMaxHp;

  public ErrorCode? ValidateAttackTarget(PlayerId attackerOwner, UnitType attackerType, AttackTargetInfo? target)
  {
    _attackValidations.Add(new AttackValidationCall(attackerOwner, attackerType, target));
    if (AcceptEverything)
    {
      return null;
    }

    return (attackerType, target) switch
    {
      (not UnitType.Soldier, _) => CommonErrors.IncompatibleEntity,
      (_, null) => CommonErrors.EntityNotFound,
      (_, { Kind: EntityKind.Tree }) => CombatErrors.InvalidTarget,
      (_, { Owner: var owner }) when owner == attackerOwner => CombatErrors.FriendlyTarget,
      _ => null,
    };
  }
}

/// <summary>Un pedido que Simulation le hizo a <see cref="FakeCombatRules.ValidateAttackTarget"/>.</summary>
internal sealed record AttackValidationCall(PlayerId AttackerOwner, UnitType AttackerType, AttackTargetInfo? Target);

/// <summary>Un pedido que Simulation le hizo a <see cref="FakeConstructionRules.EvaluateRepairStart"/>.</summary>
internal sealed record RepairStartCall(PlayerId Requester, RepairTargetInfo? Target);

/// <summary>Un pedido que Simulation le hizo a <see cref="FakeConstructionRules.EvaluateContinueBuild"/>.</summary>
internal sealed record ContinueBuildCall(PlayerId Requester, ConstructionTargetInfo? Target);
