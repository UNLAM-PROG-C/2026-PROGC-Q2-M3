using Rts.Networking.Admission;

namespace Rts.Networking.Tests.Admission;

public class TokenBucketRateLimiterTests
{
  private const int Capacity = 5;
  private const int RefillPerSecond = 2;

  private readonly ManualTimeProvider _time = new();

  private TokenBucketRateLimiter CreateLimiter() => new(Capacity, RefillPerSecond, _time);

  // Cuántas fichas se pueden gastar seguidas, sin que pase el tiempo.
  private static int Drain(TokenBucketRateLimiter limiter, int attempts = 100)
  {
    int acquired = 0;
    for (int i = 0; i < attempts && limiter.TryAcquire(); i++)
    {
      acquired++;
    }

    return acquired;
  }

  [Fact]
  public void TryAcquire_FullBucket_AllowsCapacityCommandsInARow()
  {
    var limiter = CreateLimiter();

    for (int i = 0; i < Capacity; i++)
    {
      Assert.True(limiter.TryAcquire(), $"El comando {i + 1} debería pasar.");
    }
  }

  [Fact]
  public void TryAcquire_EmptyBucket_RejectsTheNextCommand()
  {
    var limiter = CreateLimiter();
    Drain(limiter);

    Assert.False(limiter.TryAcquire());
    Assert.False(limiter.TryAcquire());
  }

  [Fact]
  public void TryAcquire_AfterOneSecond_RecoversRefillPerSecondTokens()
  {
    var limiter = CreateLimiter();
    Drain(limiter);

    _time.Advance(TimeSpan.FromSeconds(1));

    Assert.Equal(RefillPerSecond, Drain(limiter));
  }

  [Fact]
  public void TryAcquire_AfterHalfASecond_RecoversPartialRefill()
  {
    var limiter = CreateLimiter();
    Drain(limiter);

    _time.Advance(TimeSpan.FromMilliseconds(500));

    Assert.Equal(1, Drain(limiter));
  }

  // Un intento rechazado no pierde la fracción de ficha que ya se había recargado.
  [Fact]
  public void TryAcquire_RejectedAttemptsKeepTheAccumulatedFraction()
  {
    var limiter = CreateLimiter();
    Drain(limiter);

    _time.Advance(TimeSpan.FromMilliseconds(250));
    Assert.False(limiter.TryAcquire());

    _time.Advance(TimeSpan.FromMilliseconds(250));
    Assert.True(limiter.TryAcquire());
    Assert.False(limiter.TryAcquire());
  }

  [Fact]
  public void TryAcquire_AfterAVeryLongPause_NeverExceedsCapacity()
  {
    var limiter = CreateLimiter();
    Drain(limiter);

    _time.Advance(TimeSpan.FromHours(1));

    Assert.Equal(Capacity, Drain(limiter));
  }

  [Fact]
  public void TryAcquire_BucketThatWasNeverUsed_StillHoldsOnlyCapacityAfterAPause()
  {
    var limiter = CreateLimiter();

    _time.Advance(TimeSpan.FromHours(1));

    Assert.Equal(Capacity, Drain(limiter));
  }

  // El ejemplo de la pestaña Admisión del HTML: capacidad 5, recarga 2 por segundo.
  [Fact]
  public void TryAcquire_FollowsTheDocumentedTimeline()
  {
    var limiter = CreateLimiter();

    Assert.Equal(5, Drain(limiter, attempts: 6));

    _time.Advance(TimeSpan.FromSeconds(0.5));
    Assert.Equal(1, Drain(limiter, attempts: 1));

    _time.Advance(TimeSpan.FromSeconds(1));
    Assert.Equal(2, Drain(limiter, attempts: 3));

    _time.Advance(TimeSpan.FromSeconds(10));
    Assert.Equal(Capacity, Drain(limiter));
  }

  [Theory]
  [InlineData(0, 2)]
  [InlineData(-1, 2)]
  [InlineData(5, 0)]
  [InlineData(5, -1)]
  public void Constructor_NonPositiveParameter_Throws(int capacity, int refillPerSecond)
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(capacity, refillPerSecond, _time));
  }

  [Fact]
  public void Constructor_NullTimeProvider_Throws()
  {
    Assert.Throws<ArgumentNullException>(() => new TokenBucketRateLimiter(Capacity, RefillPerSecond, null!));
  }
}
