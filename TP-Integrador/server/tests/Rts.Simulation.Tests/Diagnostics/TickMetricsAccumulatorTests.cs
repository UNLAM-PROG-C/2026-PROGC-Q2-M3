using Rts.Shared.Ids;
using Rts.Simulation.Diagnostics;

namespace Rts.Simulation.Tests.Diagnostics;

/// <summary>Pruebas de <see cref="TickMetricsAccumulator"/>: promedios, máximos, fases, sobrecargas y ventanas (06 §10, D-098).</summary>
public class TickMetricsAccumulatorTests
{
  private const int Dequeued = 3;
  private const int Accepted = 2;
  private const int Rejected = 1;
  private const int Duplicates = 1;
  private const int Events = 4;

  private static readonly TickId Tick1 = TickId.Zero.Next();
  private static readonly TickId Tick2 = Tick1.Next();
  private static readonly TickId Tick3 = Tick2.Next();

  [Fact]
  public void Summary_HasTheRangeAveragesMaximaAndTotalsOfTheWindow()
  {
    var accumulator = new TickMetricsAccumulator();
    accumulator.Add(Metrics(Tick1, totalMs: 10, barrierMs: 2));
    accumulator.Add(Metrics(Tick2, totalMs: 30, barrierMs: 6));

    TickMetricsSummary summary = accumulator.TakeSummary();

    Assert.Equal((Tick1, Tick2, 2), (summary.FirstTick, summary.LastTick, summary.TickCount));
    Assert.Equal((Ms(20), Ms(30)), (summary.AverageTotal, summary.MaxTotal));
    Assert.Equal((Ms(4), Ms(6)), (summary.AverageBarrierWait, summary.MaxBarrierWait));
    Assert.Equal(
        (2 * Dequeued, 2 * Accepted, 2 * Rejected, 2 * Duplicates, 2 * Events),
        (summary.CommandsDequeued, summary.CommandsAccepted, summary.CommandsRejected, summary.DuplicatesReplied, summary.EventsPublished));
  }

  [Fact]
  public void PhaseAverages_AreInPhaseOrderAndCountOnlyTheTicksWhereThePhaseRan()
  {
    var accumulator = new TickMetricsAccumulator();
    accumulator.Add(Metrics(Tick1, phases: [(SimulationPhase.Movement, 2), (SimulationPhase.CommandBatch, 4)]));
    accumulator.Add(Metrics(Tick2, phases: [(SimulationPhase.CommandBatch, 2)])); // Descartado: falló antes de Movement.

    TickMetricsSummary summary = accumulator.TakeSummary();

    Assert.Equal(
        [new PhaseTiming(SimulationPhase.CommandBatch, Ms(3)), new PhaseTiming(SimulationPhase.Movement, Ms(2))],
        summary.AveragePhases);
  }

  [Fact]
  public void Overruns_AreCountedSoTheFirstOfTheWindowCanBeSpotted()
  {
    var accumulator = new TickMetricsAccumulator();

    accumulator.Add(Metrics(Tick1));
    int beforeAnyOverrun = accumulator.OverrunsInWindow;
    accumulator.Add(Metrics(Tick2, overrun: true));
    int afterFirst = accumulator.OverrunsInWindow;
    accumulator.Add(Metrics(Tick3, overrun: true));

    Assert.Equal((0, 1, 2), (beforeAnyOverrun, afterFirst, accumulator.OverrunsInWindow));
    Assert.Equal(2, accumulator.TakeSummary().Overruns);
  }

  [Fact]
  public void TakeSummary_StartsANewEmptyWindow()
  {
    var accumulator = new TickMetricsAccumulator();
    accumulator.Add(Metrics(Tick1, overrun: true));
    accumulator.Add(Metrics(Tick2));

    accumulator.TakeSummary();
    bool emptyAfterTaking = accumulator.IsEmpty;
    int overrunsAfterTaking = accumulator.OverrunsInWindow;
    accumulator.Add(Metrics(Tick3, totalMs: 7));
    TickMetricsSummary next = accumulator.TakeSummary();

    Assert.True(emptyAfterTaking);
    Assert.Equal(0, overrunsAfterTaking);
    Assert.Equal((Tick3, Tick3, 1, Ms(7)), (next.FirstTick, next.LastTick, next.TickCount, next.AverageTotal));
  }

  [Fact]
  public void TakeSummary_OfAnEmptyWindow_Throws()
  {
    Assert.Throws<InvalidOperationException>(new TickMetricsAccumulator().TakeSummary);
  }

  private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

  private static TickMetrics Metrics(
      TickId tick,
      int totalMs = 1,
      int barrierMs = 0,
      bool overrun = false,
      (SimulationPhase Phase, int Ms)[]? phases = null) =>
      new(
          tick,
          Ms(totalMs),
          [.. (phases ?? []).Select(phase => new PhaseTiming(phase.Phase, Ms(phase.Ms)))],
          Ms(barrierMs),
          Dequeued,
          Accepted,
          Rejected,
          Duplicates,
          Events,
          overrun);
}
