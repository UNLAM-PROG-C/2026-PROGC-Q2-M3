using Rts.Economy.Configuration;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Economy.Calculations;

/// <summary>
/// Recolección y ciclo de vida de los árboles (contrato 01 §4, reglas 1 a 7). Lee una entrada inmutable y devuelve una
/// propuesta; no comparte estado con <see cref="FarmProductionCalculator"/>, por eso pueden correr en paralelo.
/// </summary>
internal sealed class GatheringCalculator : IGatheringCalculation
{
  private const int CounterStep = 1;
  private const int CounterReset = 0;
  private const int NoWood = 0;

  private readonly int _woodPerGather;
  private readonly int _gatherIntervalTicks;
  private readonly int _treeRespawnTicks;
  private readonly int _maxWoodPerTree;

  public GatheringCalculator(EconomyConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    _woodPerGather = config.WoodPerGather;
    _gatherIntervalTicks = config.GatherIntervalTicks;
    _treeRespawnTicks = config.TreeRespawnTicks;
    _maxWoodPerTree = config.MaxWoodPerTree;
  }

  public GatheringOutcome Calculate(TickId tick, EquatableArray<GathererState> gatherers, EquatableArray<TreeEconomyState> trees)
  {
    ILookup<EntityId, GathererState> workersByTree = gatherers.ToLookup(g => g.TreeId);
    var accumulator = new GatheringAccumulator();
    foreach (TreeEconomyState tree in trees.OrderBy(t => t.TreeId))
    {
      // Regla 3: las solicitudes sobre un mismo árbol se atienden por WorkerId ascendente.
      GathererState[] workers = [.. workersByTree[tree.TreeId].OrderBy(w => w.WorkerId)];
      ProcessTree(tick, tree, workers, accumulator);
    }

    return accumulator.ToOutcome();
  }

  private void ProcessTree(TickId tick, TreeEconomyState tree, GathererState[] workers, GatheringAccumulator accumulator)
  {
    if (tree.Status == TreeStatus.Depleted)
    {
      ProcessDepletedTree(tick, tree, workers, accumulator);
      return;
    }

    TreeHarvest harvest = DistributeTreeWood(tree, workers, accumulator);
    ApplyTreeOutcome(tick, tree, workers, harvest, accumulator);
  }

  /// <summary>
  /// Regla 5: nadie recolecta de un árbol que el snapshot muestra agotado; sus recolectores pasan a Idle (caso defensivo).
  /// Reglas 6 y 7: regenera en <c>DepletedAtTick + TreeRespawnTicks</c>, pero en ese tick tampoco se recolecta de él.
  /// Se usa <c>&gt;=</c> para que, si por algún motivo no se confirmó a tiempo, regenere en el siguiente tick.
  /// </summary>
  private void ProcessDepletedTree(TickId tick, TreeEconomyState tree, GathererState[] workers, GatheringAccumulator accumulator)
  {
    accumulator.WorkersToIdle.AddRange(workers.Select(w => w.WorkerId));
    if (tree.DepletedAtTick is { } depletedAt && tick - depletedAt >= _treeRespawnTicks)
    {
      accumulator.TreeUpdates.Add(TreeEconomyState.Available(tree.TreeId, _maxWoodPerTree));
      accumulator.Events.Add(new TreeRespawned(tree.TreeId));
    }
  }

  /// <summary>
  /// Reglas 1 a 3: cada recolector suma un tick; quien completa el intervalo pide <c>WoodPerGather</c>, recibe
  /// <c>min(pedido, restante)</c> y su contador vuelve a 0. Devuelve la madera que queda y los contadores nuevos.
  /// </summary>
  private TreeHarvest DistributeTreeWood(TreeEconomyState tree, GathererState[] workers, GatheringAccumulator accumulator)
  {
    int woodLeft = tree.WoodRemaining;
    var advanced = new List<GathererState>(workers.Length);
    foreach (GathererState worker in workers)
    {
      bool completesInterval = worker.TicksAccumulated + CounterStep >= _gatherIntervalTicks;
      int amount = completesInterval ? Math.Min(_woodPerGather, woodLeft) : NoWood;
      woodLeft -= amount;
      RecordWood(worker, amount, accumulator);
      advanced.Add(WithCounter(worker, completesInterval ? CounterReset : worker.TicksAccumulated + CounterStep));
    }

    return new TreeHarvest(woodLeft, [.. advanced]);
  }

  /// <summary>Regla 3: se acepta recolección parcial, pero una asignación de 0 no genera registro.</summary>
  private static void RecordWood(GathererState worker, int amount, GatheringAccumulator accumulator)
  {
    if (amount > NoWood)
    {
      accumulator.WoodGathered.Add(new WoodGathered(worker.WorkerId, worker.Owner, worker.TreeId, amount));
    }
  }

  /// <summary>
  /// Regla 4: si el árbol llega a 0 queda <c>Depleted</c> en este tick y TODOS sus recolectores pasan a Idle, hayan
  /// completado o no su intervalo. Si no, siguen con su contador nuevo y el árbol se actualiza solo si cambió.
  /// </summary>
  private static void ApplyTreeOutcome(
      TickId tick, TreeEconomyState tree, GathererState[] workers, TreeHarvest harvest, GatheringAccumulator accumulator)
  {
    if (harvest.WoodLeft == NoWood)
    {
      accumulator.WorkersToIdle.AddRange(workers.Select(w => w.WorkerId));
      accumulator.TreeUpdates.Add(TreeEconomyState.Depleted(tree.TreeId, tick));
      accumulator.Events.Add(new TreeDepleted(tree.TreeId));
      return;
    }

    accumulator.GathererUpdates.AddRange(harvest.AdvancedWorkers);
    if (harvest.WoodLeft != tree.WoodRemaining)
    {
      accumulator.TreeUpdates.Add(TreeEconomyState.Available(tree.TreeId, harvest.WoodLeft));
    }
  }

  private static GathererState WithCounter(GathererState worker, int ticksAccumulated) =>
      new(worker.WorkerId, worker.Owner, worker.TreeId, ticksAccumulated);

  /// <summary>Lo que dejó el reparto de un árbol en este tick.</summary>
  private sealed record TreeHarvest(int WoodLeft, GathererState[] AdvancedWorkers);
}
