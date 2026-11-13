using Rts.Combat;
using Rts.Combat.Model;
using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Economy;
using Rts.Economy.Contracts;
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

  public ResourceAmount GetFarmRepairCost() => throw NotYet();

  public ResourceAmount GetTrainingCost(UnitType unitType) => unitType == UnitType.Worker ? WorkerCost : SoldierCost;

  /// <summary>Regla simple de prueba: alcanza si ninguna componente del saldo queda negativa.</summary>
  public SpendEvaluation EvaluateSpend(ResourceAmount balance, ResourceAmount cost)
  {
    ResourceAmount after = balance - cost;
    return after.IsNonNegative ? SpendEvaluation.Affordable(after) : SpendEvaluation.Insufficient(balance, EconomyErrors.InsufficientResources);
  }

  public bool CanAfford(ResourceAmount balance, ResourceAmount cost) => throw NotYet();

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

  public ErrorCode? EvaluateContinueBuild(PlayerId requester, ConstructionTargetInfo? target) => throw NotYet();

  public RepairStartEvaluation EvaluateRepairStart(PlayerId requester, RepairTargetInfo? target) => throw NotYet();

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
