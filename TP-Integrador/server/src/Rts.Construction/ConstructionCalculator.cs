using System.Collections.Immutable;
using Rts.Construction.Configuration;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Construction;

/// <summary>
/// Calculador del progreso de obras y reparaciones de granja en el tick (fase 3, contrato 03 §5).
/// </summary>
public sealed class ConstructionCalculator : IConstructionCalculator
{
  private const int NoWorkers = 0;

  private readonly ConstructionConfig _config;

  public ConstructionCalculator(ConstructionConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    _config = config;
  }

  /// <inheritdoc />
  public ConstructionTickResult CalculateTick(ConstructionTickInput input)
  {
    ArgumentNullException.ThrowIfNull(input);

    var buildingProgress = ImmutableArray.CreateBuilder<BuildingProgress>();
    var repairProgress = ImmutableArray.CreateBuilder<RepairProgress>();
    var events = ImmutableArray.CreateBuilder<IDomainEventPayload>();

    ProcessBuildings(input, buildingProgress, events);
    ProcessRepairs(input, repairProgress, events);

    return new ConstructionTickResult(
        input.Tick,
        new EquatableArray<BuildingProgress>(buildingProgress.ToImmutable()),
        new EquatableArray<RepairProgress>(repairProgress.ToImmutable()),
        new EquatableArray<IDomainEventPayload>(events.ToImmutable()));
  }

  private void ProcessBuildings(
      ConstructionTickInput input,
      ImmutableArray<BuildingProgress>.Builder progressList,
      ImmutableArray<IDomainEventPayload>.Builder events)
  {
    Dictionary<EntityId, int> counts = CountWorkersByTarget(input.Builders);
    foreach (BuildingUnderConstruction building in input.Buildings.OrderBy(b => b.BuildingId))
    {
      if (counts.TryGetValue(building.BuildingId, out int count) && count > NoWorkers)
      {
        ApplyBuildingWork(building, count, progressList, events);
      }
    }
  }

  private void ApplyBuildingWork(
      BuildingUnderConstruction building,
      int workerCount,
      ImmutableArray<BuildingProgress>.Builder progressList,
      ImmutableArray<IDomainEventPayload>.Builder events)
  {
    int maxHp = _config.BuildingMaxHp[building.BuildingType];
    int newHp = Math.Min(maxHp, building.Hp + (workerCount * _config.BuildHpPerWorkerTick));
    bool completed = newHp >= maxHp;

    progressList.Add(new BuildingProgress(building.BuildingId, newHp, completed));
    IDomainEventPayload evt = completed
        ? new BuildingCompleted(building.BuildingId)
        : new BuildingProgressChanged(building.BuildingId, newHp);
    events.Add(evt);
  }

  private void ProcessRepairs(
      ConstructionTickInput input,
      ImmutableArray<RepairProgress>.Builder progressList,
      ImmutableArray<IDomainEventPayload>.Builder events)
  {
    Dictionary<EntityId, int> counts = CountWorkersByTarget(input.Repairers);
    foreach (FarmRepairState repair in input.Repairs.OrderBy(r => r.FarmId))
    {
      if (counts.TryGetValue(repair.FarmId, out int count) && count > NoWorkers)
      {
        ApplyRepairWork(repair, count, progressList, events);
      }
    }
  }

  private void ApplyRepairWork(
      FarmRepairState repair,
      int workerCount,
      ImmutableArray<RepairProgress>.Builder progressList,
      ImmutableArray<IDomainEventPayload>.Builder events)
  {
    int newWork = repair.WorkDone + (workerCount * _config.RepairWorkPerWorkerTick);
    bool completed = newWork >= _config.RepairWorkRequired;

    progressList.Add(new RepairProgress(repair.FarmId, newWork, completed));
    IDomainEventPayload evt = completed
        ? new FarmRepairCompleted(repair.FarmId)
        : new FarmRepairProgressChanged(repair.FarmId, newWork, _config.RepairWorkRequired);
    events.Add(evt);
  }

  private static Dictionary<EntityId, int> CountWorkersByTarget(
      EquatableArray<WorkerContribution> workers)
  {
    var counts = new Dictionary<EntityId, int>();
    foreach (WorkerContribution worker in workers)
    {
      counts[worker.TargetId] = counts.GetValueOrDefault(worker.TargetId) + 1;
    }

    return counts;
  }
}

