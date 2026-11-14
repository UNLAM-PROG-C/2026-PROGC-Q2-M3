using Rts.Production.Calculation;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production;

/// <summary>
/// Implementación de <see cref="IProductionCalculator"/>: la fase 8 del tick (contrato 05 §4).
/// <para>
/// Es una <b>función pura</b>: no muta la entrada, no cobra comida ni crea unidades (eso lo confirma Simulation) y, con las mismas
/// entradas, devuelve siempre lo mismo, sin importar el orden de las colecciones. Trabaja sobre copias locales de cada productor
/// (<see cref="ProducerWork"/>) en cuatro pasos, siempre en este orden:
/// </para>
/// <list type="number">
/// <item><b>Progreso:</b> toda tarea en entrenamiento resta un tick. Va antes de los inicios, así una tarea iniciada en el tick
/// N avanza por primera vez en N+1 y, con duración D, aparece en N+D (D-037).</item>
/// <item><b>Apariciones:</b> las tareas terminadas, por <c>Sequence</c> ascendente. Sin celda libre quedan en
/// <c>PendingSpawn</c>, sin volver a cobrarse.</item>
/// <item><b>Inicios:</b> solo los productores libres <b>al entrar</b>, por <c>Sequence</c> ascendente y con prioridad estricta:
/// si a un jugador no le alcanza la comida para su solicitud más antigua, ninguna posterior suya inicia (D-038).</item>
/// <item><b>Resultado:</b> los estados que cambiaron y un <c>ProductionChanged</c> por cada productor cuyo estado discreto cambió.</item>
/// </list>
/// Solo actúan los productores operativos en este tick (un Cuartel completado en N opera desde N+1).
/// </summary>
public sealed class ProductionCalculator : IProductionCalculator
{
  private readonly IProductionRules _rules;
  private readonly IReadOnlyDictionary<UnitType, int> _foodCosts;

  /// <param name="rules">Duraciones y búsqueda de celda de aparición.</param>
  /// <param name="foodCosts">
  /// Costo en comida de cada unidad. Es de Economy (D-015) y se inyecta acá porque Production no depende de ese módulo:
  /// la composición pasa <c>EconomyConfig.UnitFoodCosts</c>, así lo que se cobra coincide con lo que Simulation verifica.
  /// </param>
  /// <exception cref="ArgumentException">Si falta el costo de algún tipo de unidad o es negativo.</exception>
  public ProductionCalculator(IProductionRules rules, IReadOnlyDictionary<UnitType, int> foodCosts)
  {
    ArgumentNullException.ThrowIfNull(rules);
    ArgumentNullException.ThrowIfNull(foodCosts);
    EnsureCostsAreValid(foodCosts);
    _rules = rules;
    _foodCosts = new Dictionary<UnitType, int>(foodCosts);
  }

  /// <inheritdoc/>
  public ProductionTickResult CalculateTick(ProductionTickInput input)
  {
    ArgumentNullException.ThrowIfNull(input);
    List<ProducerWork> works = OperationalProducers(input);
    works.ForEach(work => work.Advance());
    List<SpawnProposal> spawns = ProcessSpawns(works, input.Spawn);
    List<ProductionStart> starts = ProcessStarts(works, input);
    return BuildResult(input.Tick, works, starts, spawns);
  }

  private static void EnsureCostsAreValid(IReadOnlyDictionary<UnitType, int> foodCosts)
  {
    IEnumerable<string> missing = Enum.GetValues<UnitType>().Where(type => !foodCosts.ContainsKey(type)).Select(type => $"Falta el costo de {type}.");
    IEnumerable<string> negative = foodCosts.Where(entry => entry.Value < 0).Select(entry => $"Costo negativo para {entry.Key} ({entry.Value}).");
    string[] problems = [.. missing, .. negative];
    if (problems.Length > 0)
    {
      throw new ArgumentException("Costos de entrenamiento inválidos: " + string.Join(" ", problems), nameof(foodCosts));
    }
  }

  /// <summary>Copias de trabajo de los productores operativos, por <c>ProducerId</c>: el orden de la entrada no influye.</summary>
  private static List<ProducerWork> OperationalProducers(ProductionTickInput input) =>
      input.Producers
          .Where(producer => producer.IsOperationalAt(input.Tick))
          .OrderBy(producer => producer.ProducerId)
          .Select(producer => new ProducerWork(producer))
          .ToList();

  /// <summary>
  /// Las unidades entrenadas aparecen por <c>Sequence</c> ascendente (D-041). Cada celda elegida se reserva para que ninguna otra
  /// aparición de este tick la repita; si no hay celda, la tarea queda esperando (<c>PendingSpawn</c>) sin cobrarse de nuevo.
  /// </summary>
  private List<SpawnProposal> ProcessSpawns(List<ProducerWork> works, SpawnView view)
  {
    var reserved = new HashSet<GridCell>();
    var spawns = new List<SpawnProposal>();
    foreach (ProducerWork work in works.Where(work => work.IsFinished).OrderBy(work => work.Job!.Request.Sequence).ToList())
    {
      if (TrySpawn(work, view, reserved) is { } spawn)
      {
        spawns.Add(spawn);
      }
    }

    return spawns;
  }

  private SpawnProposal? TrySpawn(ProducerWork work, SpawnView view, HashSet<GridCell> reserved)
  {
    if (_rules.FindSpawnCell(work.Original.Footprint, view, reserved) is not { } cell)
    {
      return null;
    }

    ProductionRequest request = work.Job!.Request;
    reserved.Add(cell);
    work.Release();
    return new SpawnProposal(work.Original.ProducerId, request.Owner, request.Sequence, request.UnitType, cell, view.Grid.CellCenter(cell));
  }

  /// <summary>
  /// Candidatos: la cabeza de la cola de cada productor libre al entrar, ordenados por <c>Sequence</c>. Se lleva la comida de cada
  /// jugador y un jugador que no puede pagar su solicitud más antigua queda bloqueado el resto de la fase (D-038).
  /// </summary>
  private List<ProductionStart> ProcessStarts(List<ProducerWork> works, ProductionTickInput input)
  {
    Dictionary<PlayerId, int> food = input.Food.ToDictionary(entry => entry.Player, entry => entry.Food);
    var blocked = new HashSet<PlayerId>();
    var starts = new List<ProductionStart>();
    foreach (ProducerWork work in works.Where(work => work.WasFree && work.Head is not null).OrderBy(work => work.Head!.Sequence).ToList())
    {
      if (TryStart(work, food, blocked, input.Tick) is { } start)
      {
        starts.Add(start);
      }
    }

    return starts;
  }

  private ProductionStart? TryStart(ProducerWork work, Dictionary<PlayerId, int> food, HashSet<PlayerId> blocked, TickId tick)
  {
    ProductionRequest head = work.Head!;
    int cost = _foodCosts[head.UnitType];
    if (blocked.Contains(head.Owner) || food[head.Owner] < cost)
    {
      blocked.Add(head.Owner);
      return null;
    }

    food[head.Owner] -= cost;
    work.Start(cost, tick, _rules.GetTrainingDurationTicks(head.UnitType));
    return new ProductionStart(work.Original.ProducerId, head.Owner, head.Sequence, head.UnitType, cost);
  }

  /// <summary>Todo por ID o <c>Sequence</c> ascendente, para que el resultado no dependa del orden de las entradas (05 §4.6).</summary>
  private static ProductionTickResult BuildResult(TickId tick, List<ProducerWork> works, List<ProductionStart> starts, List<SpawnProposal> spawns) => new(
      tick,
      starts.ToEquatableArray(),
      spawns.ToEquatableArray(),
      works.Where(work => work.HasChanged).Select(work => work.Current).ToEquatableArray(),
      works.Where(work => work.DiscreteStateChanged).Select(work => (IDomainEventPayload)work.ToEvent()).ToEquatableArray());
}
