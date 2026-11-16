using Microsoft.Extensions.Logging;
using Rts.Server.Diagnostics;
using Rts.Shared.Ids;
using Rts.Simulation.Configuration;
using Rts.Simulation.Diagnostics;

namespace Rts.Server.Tests.Diagnostics;

/// <summary>
/// Pruebas del adaptador que escribe el diagnóstico de Simulation en el log (D-098, D-100): qué sale en cada nivel, cuándo sale el
/// resumen, qué sobrecargas se avisan y cómo se ve una falla crítica.
/// </summary>
public class LoggingSimulationDiagnosticsTests
{
  private const int SummaryInterval = 3;
  private const string ResumenT1ToT3 = "Resumen T1-T3 (3 ticks)";

  private static readonly SimulationConfig BaseConfig = new()
  {
    MaxCommandsPerTick = 32,
    CommandQueueCapacity = 256,
    BarrierTimeout = TimeSpan.FromSeconds(1),
    TickStallTimeout = TimeSpan.FromSeconds(2),
    SummaryIntervalTicks = SummaryInterval,
  };

  [Fact]
  public void EverySummaryInterval_WritesTheSummaryInTwoInformationLines()
  {
    var (diagnostics, log) = Create();

    ReportTicks(diagnostics, from: 1, count: SummaryInterval);
    int linesAfterFirstWindow = log.Entries.Count;
    ReportTicks(diagnostics, from: SummaryInterval + 1, count: SummaryInterval - 1); // Ventana incompleta: todavía nada.

    IReadOnlyList<LoggedEntry> info = log.At(LogLevel.Information);
    Assert.Equal(2, linesAfterFirstWindow);
    Assert.Equal(2, info.Count);
    Assert.StartsWith(ResumenT1ToT3, info[0].Message, StringComparison.Ordinal);
    Assert.StartsWith("Fases T1-T3 (prom ms): CommandBatch", info[1].Message, StringComparison.Ordinal);
    Assert.All(log.Entries, entry => Assert.Equal(LoggingSimulationDiagnostics.CategoryName, entry.Category));
  }

  [Fact]
  public void Summary_FormatsMillisecondsWithOneDecimalAndADot()
  {
    var (diagnostics, log) = Create();

    ReportTicks(diagnostics, from: 1, count: SummaryInterval, totalMs: 3.21);

    Assert.Contains("tick prom 3.2 ms", log.At(LogLevel.Information)[0].Message, StringComparison.Ordinal); // Igual en cualquier idioma.
  }

  [Fact]
  public void FinalSummary_WritesWhatIsLeftOnlyOnce()
  {
    var (diagnostics, log) = Create();
    ReportTicks(diagnostics, from: 1, count: SummaryInterval + 2);

    diagnostics.WriteFinalSummary();
    int linesAfterFinal = log.Entries.Count;
    diagnostics.WriteFinalSummary(); // Ya no queda nada: no escribe.

    Assert.Equal(linesAfterFinal, log.Entries.Count);
    Assert.StartsWith("Resumen T4-T5 (2 ticks)", log.At(LogLevel.Information)[2].Message, StringComparison.Ordinal);
  }

  [Fact]
  public void OnlyTheFirstOverrunOfEachWindow_IsWarnedRightAway()
  {
    var (diagnostics, log) = Create();

    diagnostics.OnTickCompleted(Metrics(1, overrun: true));
    diagnostics.OnTickCompleted(Metrics(2, overrun: true)); // Misma ventana: solo se cuenta.
    diagnostics.OnTickCompleted(Metrics(3));
    diagnostics.OnTickCompleted(Metrics(4, overrun: true)); // Ventana nueva: se avisa otra vez.

    IReadOnlyList<LoggedEntry> warnings = log.At(LogLevel.Warning);
    Assert.Equal(2, warnings.Count);
    Assert.StartsWith("Sobrecarga en T1: la vuelta no entró en 50.0 ms", warnings[0].Message, StringComparison.Ordinal);
    Assert.StartsWith("Sobrecarga en T4", warnings[1].Message, StringComparison.Ordinal);
    Assert.Contains("sobrecargas 2", log.At(LogLevel.Information)[0].Message, StringComparison.Ordinal); // El resumen da el total.
  }

  [Theory]
  [InlineData(DiagnosticsLevel.Summary, 0)]
  [InlineData(DiagnosticsLevel.Debug, SummaryInterval)]
  public void DebugLevel_AddsOneLinePerTick(DiagnosticsLevel level, int expectedDebugLines)
  {
    var (diagnostics, log) = Create(level);

    ReportTicks(diagnostics, from: 1, count: SummaryInterval);

    Assert.Equal(expectedDebugLines, log.At(LogLevel.Debug).Count);
    Assert.Equal(2, log.At(LogLevel.Information).Count); // El resumen sale en los dos niveles.
  }

  [Fact]
  public void CriticalFailure_IsAnErrorWithKindTickPhaseAndTheException()
  {
    var (diagnostics, log) = Create();
    var cause = new TimeoutException("la barrera venció");
    var report = new CriticalFailureReport(
        CriticalFailureKind.PhaseFailed, new TickId(512), new TickId(511), SimulationPhase.EconomyAndCombat, "TimeoutException: la barrera venció", cause);

    diagnostics.OnCriticalFailure(report);

    LoggedEntry error = Assert.Single(log.At(LogLevel.Error));
    Assert.Equal(
        "Falla crítica PhaseFailed en T512 (fase EconomyAndCombat), último confirmado T511: TimeoutException: la barrera venció",
        error.Message);
    Assert.Same(cause, error.Exception); // El log de consola escribe su traza.
  }

  private static (LoggingSimulationDiagnostics Diagnostics, RecordingLoggerFactory Log) Create(
      DiagnosticsLevel level = DiagnosticsLevel.Summary)
  {
    var log = new RecordingLoggerFactory();
    return (new LoggingSimulationDiagnostics(log, BaseConfig with { Diagnostics = level }), log);
  }

  private static void ReportTicks(LoggingSimulationDiagnostics diagnostics, int from, int count, double totalMs = 1)
  {
    for (int tick = from; tick < from + count; tick++)
    {
      diagnostics.OnTickCompleted(Metrics(tick, totalMs: totalMs));
    }
  }

  private static TickMetrics Metrics(long tick, bool overrun = false, double totalMs = 1) => new(
      new TickId(tick),
      TimeSpan.FromMilliseconds(totalMs),
      [new PhaseTiming(SimulationPhase.CommandBatch, TimeSpan.FromMilliseconds(totalMs))],
      TimeSpan.Zero,
      CommandsDequeued: 0,
      CommandsAccepted: 0,
      CommandsRejected: 0,
      DuplicatesReplied: 0,
      EventsPublished: 0,
      overrun);
}
