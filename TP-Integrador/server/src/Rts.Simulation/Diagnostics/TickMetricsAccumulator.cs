using Rts.Shared.Ids;

namespace Rts.Simulation.Diagnostics;

/// <summary>
/// Acumula las <see cref="TickMetrics"/> de una ventana de ticks y la resume (06 §10, DECIDIDO D-098). Es lógica pura: no sabe de
/// niveles, de períodos ni de texto. Quien lo usa decide cuándo pedir el resumen: el adaptador del Server cada
/// <c>SummaryIntervalTicks</c> y al terminar la partida; la medición de paralelo frente a secuencial, una vez al final.
/// <para>
/// <b>No es seguro para hilos:</b> está pensado para usarse desde el hilo que recibe <c>OnTickCompleted</c>, que es siempre el del
/// loop. Pedir el resumen final después de que se completó <c>StopTicksAsync</c> es seguro, porque ya no llegan más ticks (D-097).
/// </para>
/// </summary>
public sealed class TickMetricsAccumulator
{
  private Window _window = new();

  /// <summary>Ticks sumados en la ventana actual.</summary>
  public int TickCount => _window.TickCount;

  /// <summary>
  /// Sobrecargas en la ventana actual. Si vale 1 justo después de sumar un tick con sobrecarga, es la primera de la ventana: la que
  /// se avisa enseguida en nivel <c>Summary</c>; las siguientes solo se cuentan (D-098).
  /// </summary>
  public int OverrunsInWindow => _window.Overruns;

  public bool IsEmpty => _window.TickCount == 0;

  public void Add(TickMetrics metrics)
  {
    ArgumentNullException.ThrowIfNull(metrics);
    _window.Add(metrics);
  }

  /// <summary>Devuelve el resumen de la ventana actual y empieza una nueva, vacía.</summary>
  /// <exception cref="InvalidOperationException">Si la ventana está vacía: no hay nada que resumir.</exception>
  public TickMetricsSummary TakeSummary()
  {
    if (IsEmpty)
    {
      throw new InvalidOperationException("La ventana no tiene ticks: no hay nada que resumir.");
    }

    Window finished = _window;
    _window = new Window();
    return finished.Summarize();
  }

  /// <summary>Sumas y máximos de una ventana. Reemplazarla entera por una nueva es más simple que poner a cero cada campo.</summary>
  private sealed class Window
  {
    private readonly SortedDictionary<SimulationPhase, PhaseTotals> _phases = new();
    private TickId _firstTick;
    private TickId _lastTick;
    private TimeSpan _totalSum;
    private TimeSpan _totalMax;
    private TimeSpan _barrierSum;
    private TimeSpan _barrierMax;
    private int _dequeued;
    private int _accepted;
    private int _rejected;
    private int _duplicates;
    private int _events;

    public int TickCount { get; private set; }

    public int Overruns { get; private set; }

    public void Add(TickMetrics metrics)
    {
      _firstTick = TickCount == 0 ? metrics.Tick : _firstTick;
      _lastTick = metrics.Tick;
      TickCount++;
      Overruns += metrics.IsOverrun ? 1 : 0;
      _totalSum += metrics.Total;
      _totalMax = Max(_totalMax, metrics.Total);
      _barrierSum += metrics.BarrierWait;
      _barrierMax = Max(_barrierMax, metrics.BarrierWait);
      AddCommandsAndEvents(metrics);
      AddPhases(metrics);
    }

    public TickMetricsSummary Summarize() => new(
        _firstTick,
        _lastTick,
        TickCount,
        _totalSum / TickCount,
        _totalMax,
        [.. _phases.Select(phase => new PhaseTiming(phase.Key, phase.Value.Average))],
        _barrierSum / TickCount,
        _barrierMax,
        Overruns,
        _dequeued,
        _accepted,
        _rejected,
        _duplicates,
        _events);

    private static TimeSpan Max(TimeSpan current, TimeSpan candidate) => candidate > current ? candidate : current;

    private void AddCommandsAndEvents(TickMetrics metrics)
    {
      _dequeued += metrics.CommandsDequeued;
      _accepted += metrics.CommandsAccepted;
      _rejected += metrics.CommandsRejected;
      _duplicates += metrics.DuplicatesReplied;
      _events += metrics.EventsPublished;
    }

    /// <summary>Cada fase promedia sobre los ticks en que corrió: un tick descartado por falla trae solo las fases hasta la que falló.</summary>
    private void AddPhases(TickMetrics metrics)
    {
      foreach (PhaseTiming timing in metrics.Phases)
      {
        PhaseTotals totals = _phases.GetValueOrDefault(timing.Phase);
        _phases[timing.Phase] = new PhaseTotals(totals.Sum + timing.Duration, totals.Count + 1);
      }
    }
  }

  private readonly record struct PhaseTotals(TimeSpan Sum, int Count)
  {
    public TimeSpan Average => Sum / Count;
  }
}
