using System.Collections.Immutable;
using Rts.Construction.Configuration;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Construction;

/// <summary>
/// Implementación de las reglas de validación de construcción y reparación (contrato 03).
/// </summary>
public sealed class ConstructionRules : IConstructionRules
{
  private const int MinInitialHp = 1;
  private const int MaxHpOffset = 1;

  private readonly ConstructionConfig _config;

  public ConstructionRules(ConstructionConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    _config = config;
  }

  /// <inheritdoc />
  public FootprintSize GetFootprintSize(BuildingType buildingType) =>
      _config.FootprintSizes[buildingType];

  /// <inheritdoc />
  public int GetMaxHp(BuildingType buildingType) =>
      _config.BuildingMaxHp[buildingType];

  /// <inheritdoc />
  public int GetInitialHp(BuildingType buildingType)
  {
    int maxHp = GetMaxHp(buildingType);
    int rawHp = (int)Math.Ceiling(maxHp * _config.InitialHpFraction);
    return Math.Clamp(rawHp, MinInitialHp, maxHp - MaxHpOffset);
  }

  /// <inheritdoc />
  public SiteEvaluation EvaluateSite(BuildingType buildingType, GridCell anchor, OccupancyView site)
  {
    ArgumentNullException.ThrowIfNull(site);
    if (buildingType == BuildingType.MainCenter || !Enum.IsDefined(buildingType))
    {
      return SiteEvaluation.Invalid(ConstructionErrors.NotBuildable);
    }

    var footprint = new Footprint(anchor, GetFootprintSize(buildingType));
    ErrorCode? error =
        CheckFootprintBounds(site, footprint)
        ?? CheckFootprintOccupancy(site, footprint);

    return error is { } code
        ? SiteEvaluation.Invalid(code)
        : SiteEvaluation.Valid(footprint);
  }

  /// <inheritdoc />
  public PlacementEvaluation EvaluatePlacement(PlacementInput input)
  {
    ArgumentNullException.ThrowIfNull(input);
    SiteEvaluation siteEval = EvaluateSite(input.BuildingType, input.Anchor, input.Site);
    if (siteEval.Error is { } siteError)
    {
      return PlacementEvaluation.Rejected(siteError);
    }

    EquatableArray<EntityCommandResult> workerResults = BuildWorkerResults(input.Workers);
    return workerResults.Any(r => r.IsAccepted)
        ? PlacementEvaluation.Accepted(siteEval.Footprint!.Value, GetInitialHp(input.BuildingType), workerResults)
        : PlacementEvaluation.Rejected(ConstructionErrors.NoReachableWorker, workerResults);
  }

  /// <inheritdoc />
  public ErrorCode? EvaluateContinueBuild(PlayerId requester, ConstructionTargetInfo? target)
  {
    if (target is null)
    {
      return CommonErrors.EntityNotFound;
    }

    if (target.Owner != requester)
    {
      return CommonErrors.NotOwner;
    }

    return target.IsCompleted ? ConstructionErrors.AlreadyCompleted : null;
  }

  /// <inheritdoc />
  public RepairStartEvaluation EvaluateRepairStart(PlayerId requester, RepairTargetInfo? target)
  {
    ErrorCode? error = CheckRepairTarget(requester, target);
    if (error is { } code)
    {
      return RepairStartEvaluation.Invalid(code);
    }

    RepairStartKind kind = target!.IsRepairInProgress
        ? RepairStartKind.Join
        : RepairStartKind.StartAndPay;

    return RepairStartEvaluation.Valid(kind);
  }

  private static ErrorCode? CheckFootprintBounds(OccupancyView site, Footprint footprint) =>
      site.Grid.Contains(footprint) ? null : ConstructionErrors.OutOfBounds;

  private static ErrorCode? CheckFootprintOccupancy(OccupancyView site, Footprint footprint)
  {
    if (footprint.Cells().Any(c => site.StructureCells.Contains(c)))
    {
      return ConstructionErrors.CellOccupied;
    }

    if (footprint.Cells().Any(c => site.TreeReservedCells.Contains(c)))
    {
      return ConstructionErrors.TreeReserved;
    }

    return footprint.Cells().Any(c => site.UnitCells.Contains(c))
        ? ConstructionErrors.UnitInFootprint
        : null;
  }

  private static EquatableArray<EntityCommandResult> BuildWorkerResults(
      EquatableArray<WorkerPlacementCandidate> workers)
  {
    var builder = ImmutableArray.CreateBuilder<EntityCommandResult>(workers.Count);
    foreach (WorkerPlacementCandidate worker in workers)
    {
      EntityCommandResult result = worker.MovementError is { } err
          ? EntityCommandResult.Rejected(worker.WorkerId, err)
          : EntityCommandResult.Accepted(worker.WorkerId);
      builder.Add(result);
    }

    return new EquatableArray<EntityCommandResult>(builder.MoveToImmutable());
  }

  private static ErrorCode? CheckRepairTarget(PlayerId requester, RepairTargetInfo? target)
  {
    if (target is null)
    {
      return CommonErrors.EntityNotFound;
    }

    if (target.Owner != requester)
    {
      return CommonErrors.NotOwner;
    }

    if (target.BuildingType != BuildingType.Farm || !target.IsCompleted)
    {
      return ConstructionErrors.NotAFarm;
    }

    return target.IsDepleted ? null : ConstructionErrors.FarmNotDepleted;
  }
}

