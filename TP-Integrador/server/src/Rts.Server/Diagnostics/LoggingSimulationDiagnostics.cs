using System.Globalization;
using Microsoft.Extensions.Logging;
using Rts.Simulation.Configuration;
using Rts.Simulation.Diagnostics;

namespace Rts.Server.Diagnostics;

/// <summary>
/// Traduce el diagnóstico de Simulation a texto en el log de consola (D-083, D-095). Es el único lugar que decide <b>qué</b> se
/// escribe según <see cref="SimulationConfig.Diagnostics"/> (D-098) y <b>cómo</b> se ve (D-100):
/// <list type="bullet">
/// <item>cada <see cref="SimulationConfig.SummaryIntervalTicks"/> ticks, un resumen en 2 líneas (Information);</item>
/// <item>la primera sobrecarga de cada ventana, enseguida (Warning); las siguientes solo se cuentan en el resumen;</item>
/// <item>cada falla crítica, con la traza de su excepción (Error);</item>
/// <item>en nivel <see cref="DiagnosticsLevel.Debug"/>, además, una línea por tick (Debug).</item>
/// </list>
/// Lo llaman el hilo del loop y el supervisor (D-095): el lock solo protege el acumulador y es corto, y el log de consola encola
/// los mensajes y los escribe en segundo plano, así que no bloquea el tick.
/// </summary>
public sealed class LoggingSimulationDiagnostics : ISimulationDiagnostics
{
  /// <summary>Categoría de los mensajes. <c>Program</c> la deja pasar en todos los niveles: lo que se escribe lo decide la config.</summary>
  public const string CategoryName = "Rts.Simulation";

  private const string MillisecondsFormat = "F1";
  private const string PhaseSeparator = " | ";

  private readonly ILogger _log;
  private readonly DiagnosticsLevel _level;
  private readonly int _summaryIntervalTicks;
  private readonly TimeSpan _tickInterval;
  private readonly object _gate = new();
  private readonly TickMetricsAccumulator _window = new();

  public LoggingSimulationDiagnostics(ILoggerFactory loggers, SimulationConfig config)
  {
    ArgumentNullException.ThrowIfNull(loggers);
    ArgumentNullException.ThrowIfNull(config);
    _log = loggers.CreateLogger(CategoryName);
    _level = config.Diagnostics;
    _summaryIntervalTicks = config.SummaryIntervalTicks;
    _tickInterval = config.TickRate.TickInterval;
  }

  public void OnTickCompleted(TickMetrics metrics)
  {
    ArgumentNullException.ThrowIfNull(metrics);
    TickMetricsSummary? summary = null;
    bool isFirstOverrunOfWindow;
    lock (_gate)
    {
      _window.Add(metrics);
      isFirstOverrunOfWindow = metrics.IsOverrun && _window.OverrunsInWindow == 1;
      if (_window.TickCount >= _summaryIntervalTicks)
      {
        summary = _window.TakeSummary();
      }
    }

    // Fuera del lock: escribir no tiene por qué demorar a otro que quiera acumular.
    WriteTick(metrics, isFirstOverrunOfWindow);
    if (summary is not null)
    {
      WriteSummary(summary);
    }
  }

  public void OnCriticalFailure(CriticalFailureReport report)
  {
    ArgumentNullException.ThrowIfNull(report);
    string phase = report.Phase is { } failedPhase ? $" (fase {failedPhase})" : string.Empty;
    _log.LogError(
        report.Exception,
        "Falla crítica {Kind} en {Tick}{Phase}, último confirmado {LastConfirmed}: {Reason}",
        report.Kind, report.Tick, phase, report.LastConfirmedTick, report.Reason);
  }

  /// <summary>
  /// Escribe el resumen de lo que quedó de la última ventana. Lo llama la composición del servidor cuando la partida terminó, es
  /// decir después de completar <c>StopTicksAsync</c>, cuando ya no llegan más ticks (D-097). Si no queda nada, no escribe.
  /// </summary>
  public void WriteFinalSummary()
  {
    TickMetricsSummary? summary;
    lock (_gate)
    {
      summary = _window.IsEmpty ? null : _window.TakeSummary();
    }

    if (summary is not null)
    {
      WriteSummary(summary);
    }
  }

  private static string Ms(TimeSpan duration) =>
      duration.TotalMilliseconds.ToString(MillisecondsFormat, CultureInfo.InvariantCulture);

  private void WriteTick(TickMetrics metrics, bool isFirstOverrunOfWindow)
  {
    if (_level == DiagnosticsLevel.Debug)
    {
      _log.LogDebug(
          "{Tick}: {TotalMs} ms{Overrun} | barrera {BarrierMs} ms | comandos {Dequeued} ({Accepted} aceptados, {Rejected} rechazados, "
          + "{Duplicates} duplicados) | eventos {Events}",
          metrics.Tick, Ms(metrics.Total), metrics.IsOverrun ? " (sobrecarga)" : string.Empty, Ms(metrics.BarrierWait),
          metrics.CommandsDequeued, metrics.CommandsAccepted, metrics.CommandsRejected, metrics.DuplicatesReplied, metrics.EventsPublished);
    }

    if (isFirstOverrunOfWindow)
    {
      _log.LogWarning(
          "Sobrecarga en {Tick}: la vuelta no entró en {IntervalMs} ms (cálculo {TotalMs} ms). Primera de esta ventana; el resumen dará el total.",
          metrics.Tick, Ms(_tickInterval), Ms(metrics.Total));
    }
  }

  private void WriteSummary(TickMetricsSummary summary)
  {
    _log.LogInformation(
        "Resumen {FirstTick}-{LastTick} ({TickCount} ticks): tick prom {AverageMs} ms, máx {MaxMs} ms | barrera prom {BarrierAverageMs} ms, "
        + "máx {BarrierMaxMs} ms | sobrecargas {Overruns} | comandos {Dequeued} ({Accepted} aceptados, {Rejected} rechazados, "
        + "{Duplicates} duplicados) | eventos {Events}",
        summary.FirstTick, summary.LastTick, summary.TickCount, Ms(summary.AverageTotal), Ms(summary.MaxTotal),
        Ms(summary.AverageBarrierWait), Ms(summary.MaxBarrierWait), summary.Overruns, summary.CommandsDequeued,
        summary.CommandsAccepted, summary.CommandsRejected, summary.DuplicatesReplied, summary.EventsPublished);

    string phases = string.Join(PhaseSeparator, summary.AveragePhases.Select(phase => $"{phase.Phase} {Ms(phase.Duration)}"));
    _log.LogInformation("Fases {FirstTick}-{LastTick} (prom ms): {Phases}", summary.FirstTick, summary.LastTick, phases);
  }
}
