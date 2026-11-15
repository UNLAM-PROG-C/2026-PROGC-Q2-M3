using Rts.Server.Configuration;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Simulation;
using Rts.Simulation.Diagnostics;

namespace Rts.Measurement;

/// <summary>Ticks de calentamiento (JIT y órdenes del primer tick, se descartan) y ticks medidos por carga (D-101).</summary>
public sealed record MeasurementWindow(int WarmupTicks, int MeasuredTicks)
{
  public static readonly MeasurementWindow Default = new(WarmupTicks: 20, MeasuredTicks: 120);
}

/// <summary>Lo medido en un modo. "Dominio" = fases 5 + 6 (lanzar Economy ∥ Combat, barrera y commit): lo justo para comparar.</summary>
public sealed record ModeStats(double AverageDomainMs, double MedianDomainMs, double MedianTickMs, TickMetricsSummary Summary);

/// <summary>Resultado de una carga: los dos modos, el speedup y los controles de validez.</summary>
/// <param name="WindowShortened">La ventana se acortó para terminar antes de la primera muerte (el balance cambió).</param>
/// <param name="IdenticalResults">Las publicaciones de los dos modos fueron iguales en cada tick (D-051).</param>
/// <param name="LoadStayedConstant">No murió ninguna unidad mientras se medía: la carga fue la misma en todo el período.</param>
public sealed record LoadMeasurement(
    int Units,
    int MeasuredTicks,
    bool WindowShortened,
    ModeStats Sequential,
    ModeStats Parallel,
    bool IdenticalResults,
    bool LoadStayedConstant)
{
  /// <summary>Cuántas veces más rápido es el paralelo en el bloque de dominio (medianas). Mayor que 1 = gana el paralelo.</summary>
  public double Speedup => Sequential.MedianDomainMs / Parallel.MedianDomainMs;
}

/// <summary>
/// Mide Economy ∥ Combat en paralelo frente a secuencial con un escenario de carga real (D-101). Corre las dos partidas
/// <b>intercaladas tick a tick</b>, alternando cuál va primero, para que el ruido de la máquina afecte igual a los dos modos; y
/// compara sus publicaciones en cada tick, así verifica a la vez que el paralelismo no cambia el resultado.
/// </summary>
public static class ParallelismMeasurement
{
  /// <summary>Cargas del barrido: 30 es una partida real; después, órdenes de magnitud con puntos intermedios para ubicar el cruce.</summary>
  public static readonly IReadOnlyList<int> DefaultLoads = [30, 300, 1_500, 3_000, 6_000];

  private const int TickAfterOrders = 1;

  public static LoadMeasurement Measure(ServerConfig config, int units, MeasurementWindow window)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(window);
    int measuredTicks = FitToLoadLifetime(config, window);
    FootprintSize mainCenterSize = config.Construction.FootprintSizes[BuildingType.MainCenter];
    var scenario = LoadScenario.Create(units, mainCenterSize);
    MeasuredMatch sequential = StartWithOrders(config, scenario, runDomainPhaseInParallel: false);
    MeasuredMatch parallel = StartWithOrders(config, scenario, runDomainPhaseInParallel: true);
    var pair = new MatchPair(sequential, parallel);

    pair.Advance(window.WarmupTicks, samples: null);
    int unitsAtStart = sequential.World.Units.Count;
    var samples = new PairSamples();
    pair.Advance(measuredTicks, samples);

    return new LoadMeasurement(
        units,
        measuredTicks,
        WindowShortened: measuredTicks < window.MeasuredTicks,
        samples.Sequential.ToStats(),
        samples.Parallel.ToStats(),
        pair.IdenticalResults,
        LoadStayedConstant: sequential.World.Units.Count == unitsAtStart);
  }

  /// <summary>
  /// Ticks medibles antes de que muera el primer soldado: con los dos soldados de cada bloque golpeándose, el primero cae tras
  /// <c>ceil(vida / daño)</c> golpes, uno cada <c>CooldownTicks</c>. Se calcula desde la config, así un cambio de balance no
  /// rompe la medición: la ventana se acorta y el reporte lo avisa.
  /// </summary>
  private static int FitToLoadLifetime(ServerConfig config, MeasurementWindow window)
  {
    int soldierHp = config.Combat.UnitMaxHp[UnitType.Soldier];
    int damage = config.Combat.Soldier.Damage;
    int hitsToDie = (soldierHp + damage - 1) / damage;
    int ticksBeforeFirstDeath = (hitsToDie - 1) * config.Combat.Soldier.CooldownTicks;
    int available = ticksBeforeFirstDeath - TickAfterOrders - window.WarmupTicks;
    if (available <= 0)
    {
      throw new InvalidOperationException($"Con este balance los soldados mueren antes de terminar el calentamiento ({ticksBeforeFirstDeath} ticks).");
    }

    return Math.Min(window.MeasuredTicks, available);
  }

  private static MeasuredMatch StartWithOrders(ServerConfig config, LoadScenario scenario, bool runDomainPhaseInParallel)
  {
    var match = MeasuredMatch.Start(config, scenario.Map, scenario.OrderCount, runDomainPhaseInParallel);
    match.Submit(scenario.OrdersFor(match.World));
    return match;
  }

  /// <summary>Las dos partidas, avanzadas juntas: en los ticks pares va primero la secuencial; en los impares, la paralela.</summary>
  private sealed class MatchPair(MeasuredMatch sequential, MeasuredMatch parallel)
  {
    private int _ticks;

    public bool IdenticalResults { get; private set; } = true;

    public void Advance(int ticks, PairSamples? samples)
    {
      for (int i = 0; i < ticks; i++, _ticks++)
      {
        bool sequentialFirst = _ticks % 2 == 0;
        TickOutcome first = (sequentialFirst ? sequential : parallel).Advance();
        TickOutcome second = (sequentialFirst ? parallel : sequential).Advance();
        (TickOutcome sequentialOutcome, TickOutcome parallelOutcome) = sequentialFirst ? (first, second) : (second, first);
        IdenticalResults &= Equals(sequentialOutcome.Publication, parallelOutcome.Publication);
        samples?.Add(sequentialOutcome.Metrics, parallelOutcome.Metrics);
      }
    }
  }

  private sealed class PairSamples
  {
    public ModeSamples Sequential { get; } = new();

    public ModeSamples Parallel { get; } = new();

    public void Add(TickMetrics sequential, TickMetrics parallel)
    {
      Sequential.Add(sequential);
      Parallel.Add(parallel);
    }
  }

  /// <summary>Las muestras de un modo: el acumulador del paso 20 para los promedios y las listas para las medianas.</summary>
  private sealed class ModeSamples
  {
    private readonly TickMetricsAccumulator _accumulator = new();
    private readonly List<double> _domainMs = [];
    private readonly List<double> _tickMs = [];

    public void Add(TickMetrics metrics)
    {
      _accumulator.Add(metrics);
      _domainMs.Add(metrics.Phases.Where(IsDomainPhase).Sum(phase => phase.Duration.TotalMilliseconds));
      _tickMs.Add(metrics.Total.TotalMilliseconds);
    }

    public ModeStats ToStats() => new(_domainMs.Average(), Median(_domainMs), Median(_tickMs), _accumulator.TakeSummary());

    private static bool IsDomainPhase(PhaseTiming phase) =>
        phase.Phase is SimulationPhase.EconomyAndCombat or SimulationPhase.BarrierAndCommit;

    /// <summary>El valor del medio: a diferencia del promedio, un pico aislado (el SO, el recolector de basura) no lo arrastra.</summary>
    private static double Median(List<double> values)
    {
      double[] sorted = [.. values.Order()];
      int middle = sorted.Length / 2;
      return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
  }
}
