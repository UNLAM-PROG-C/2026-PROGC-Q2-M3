using Rts.Networking.Admission;

namespace Rts.Networking.Tests.Admission;

public class ViolationTrackerTests
{
  private const int MaxViolations = 3;

  private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan Instant = TimeSpan.FromMilliseconds(1);

  private readonly ManualTimeProvider _time = new();

  private ViolationTracker CreateTracker() => new(MaxViolations, Window, _time);

  private static void RegisterTimes(ViolationTracker tracker, int count)
  {
    for (int i = 0; i < count; i++)
    {
      tracker.Register(NetworkErrors.MalformedJson);
    }
  }

  [Fact]
  public void Register_UpToMaxViolations_DoesNotAskToClose()
  {
    var tracker = CreateTracker();

    for (int i = 0; i < MaxViolations; i++)
    {
      Assert.False(tracker.Register(NetworkErrors.MalformedJson), $"La falta {i + 1} no debería cerrar.");
    }
  }

  [Fact]
  public void Register_OneMoreThanMaxViolations_AsksToClose()
  {
    var tracker = CreateTracker();
    RegisterTimes(tracker, MaxViolations);

    Assert.True(tracker.Register(NetworkErrors.RateLimited));
  }

  [Fact]
  public void Register_AfterTheWindowPasses_ForgetsOldViolations()
  {
    var tracker = CreateTracker();
    RegisterTimes(tracker, MaxViolations);

    _time.Advance(Window);

    Assert.False(tracker.Register(NetworkErrors.MalformedJson));
    Assert.Equal(1, tracker.RecentCount);
  }

  [Fact]
  public void Register_JustBeforeTheWindowEnds_StillCountsOldViolations()
  {
    var tracker = CreateTracker();
    RegisterTimes(tracker, MaxViolations);

    _time.Advance(Window - Instant);

    Assert.True(tracker.Register(NetworkErrors.MalformedJson));
  }

  [Fact]
  public void Register_SlidingWindow_ExpiresViolationsOneByOne()
  {
    var tracker = CreateTracker();
    tracker.Register(NetworkErrors.MalformedJson);
    _time.Advance(Window / 2);
    RegisterTimes(tracker, MaxViolations - 1);

    // La primera falta vence; las otras siguen dentro de la ventana.
    _time.Advance(Window / 2);

    Assert.Equal(MaxViolations - 1, tracker.RecentCount);
    Assert.False(tracker.Register(NetworkErrors.MalformedJson));
    Assert.True(tracker.Register(NetworkErrors.MalformedJson));
  }

  [Fact]
  public void RecentCount_WithoutViolations_IsZero()
  {
    Assert.Equal(0, CreateTracker().RecentCount);
  }
}
