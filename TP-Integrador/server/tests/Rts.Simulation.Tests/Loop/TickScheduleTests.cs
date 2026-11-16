using Rts.Simulation.Loop;

namespace Rts.Simulation.Tests.Loop;

public class TickScheduleTests
{
  private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);
  private static readonly TimeSpan ShortTick = TimeSpan.FromMilliseconds(10);
  private static readonly TimeSpan LateWakeUp = TimeSpan.FromMilliseconds(12);
  private static readonly TimeSpan SlowTick = TimeSpan.FromMilliseconds(130);

  [Fact]
  public void FirstTick_StartsRightAway()
  {
    var schedule = new TickSchedule(Interval);

    Assert.Equal(TimeSpan.Zero, schedule.DelayUntilNextStart(TimeSpan.Zero));
  }

  [Fact]
  public void ShortTick_WaitsTheRestOfTheInterval()
  {
    var schedule = new TickSchedule(Interval);

    bool overrun = schedule.TickFinished(ShortTick);

    Assert.False(overrun);
    Assert.Equal(Interval, schedule.NextStart);
    Assert.Equal(Interval - ShortTick, schedule.DelayUntilNextStart(ShortTick));
  }

  [Fact]
  public void LateWakeUp_DoesNotDriftTheFollowingTicks()
  {
    var schedule = new TickSchedule(Interval);
    schedule.TickFinished(ShortTick);

    // El SO despertó tarde al hilo: el tick 2 empezó en 50 + 12 ms y terminó 10 ms después.
    schedule.TickFinished(Interval + LateWakeUp + ShortTick);

    Assert.Equal(Interval * 2, schedule.NextStart); // Sigue en la grilla de 50 ms, no en 62 + 50.
  }

  [Fact]
  public void Overrun_StartsNextTickImmediatelyAndReanchors()
  {
    var schedule = new TickSchedule(Interval);

    bool overrun = schedule.TickFinished(SlowTick);

    Assert.True(overrun);
    Assert.Equal(TimeSpan.Zero, schedule.DelayUntilNextStart(SlowTick)); // Arranca ya, una sola vez.
    schedule.TickFinished(SlowTick + ShortTick);
    Assert.Equal(SlowTick + Interval, schedule.NextStart); // La grilla nueva parte de donde terminó el tick lento: sin ráfaga.
  }
}
