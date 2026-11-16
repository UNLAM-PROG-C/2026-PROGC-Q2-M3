using Rts.Networking.Connections;
using Rts.Networking.Tests.Admission;

namespace Rts.Networking.Tests.Connections;

public class HeartbeatTests
{
  private const long FirstPingId = 1;
  private const long SecondPingId = 2;
  private const long WrongPingId = 99;

  private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(3);
  private static readonly TimeSpan PongTimeout = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan Instant = TimeSpan.FromMilliseconds(1);

  private readonly ManualTimeProvider _time = new();

  private Heartbeat Create() => new(IdleInterval, PongTimeout, _time);

  /// <summary>Lleva el heartbeat hasta que envía su primer Ping.</summary>
  private Heartbeat CreateWithPendingPing()
  {
    var heartbeat = Create();
    _time.Advance(IdleInterval);
    Assert.Equal(HeartbeatStatus.PingDue, heartbeat.Check(out _));
    return heartbeat;
  }

  [Fact]
  public void Check_BeforeTheIdleInterval_IsAlive()
  {
    var heartbeat = Create();

    _time.Advance(IdleInterval - Instant);

    Assert.Equal(HeartbeatStatus.Alive, heartbeat.Check(out _));
  }

  [Fact]
  public void Check_AfterTheIdleInterval_AsksForAPingWithTheFirstId()
  {
    var heartbeat = Create();

    _time.Advance(IdleInterval);

    Assert.Equal(HeartbeatStatus.PingDue, heartbeat.Check(out long pingId));
    Assert.Equal(FirstPingId, pingId);
  }

  [Fact]
  public void Check_WithAPingPendingAndTimeLeft_IsAliveWithoutAnotherPing()
  {
    var heartbeat = CreateWithPendingPing();

    _time.Advance(PongTimeout - Instant);

    Assert.Equal(HeartbeatStatus.Alive, heartbeat.Check(out _));
  }

  [Fact]
  public void Check_WhenThePongTimeoutPasses_TimesOut()
  {
    var heartbeat = CreateWithPendingPing();

    _time.Advance(PongTimeout);

    Assert.Equal(HeartbeatStatus.TimedOut, heartbeat.Check(out _));
  }

  [Fact]
  public void RecordPong_WithThePendingId_AnswersThePingAndTheNextOneGetsTheNextId()
  {
    var heartbeat = CreateWithPendingPing();

    heartbeat.RecordPong(FirstPingId);

    // Si el Ping siguiera pendiente, Check diría Alive o TimedOut; como se respondió, toca el siguiente.
    _time.Advance(IdleInterval);
    Assert.Equal(HeartbeatStatus.PingDue, heartbeat.Check(out long pingId));
    Assert.Equal(SecondPingId, pingId);
  }

  [Fact]
  public void RecordPong_WithAnotherId_IsIgnoredAndThePingStillTimesOut()
  {
    var heartbeat = CreateWithPendingPing();

    heartbeat.RecordPong(WrongPingId);
    _time.Advance(PongTimeout);

    Assert.Equal(HeartbeatStatus.TimedOut, heartbeat.Check(out _));
  }

  [Fact]
  public void RecordActivity_RestartsTheIdleInterval()
  {
    var heartbeat = Create();
    _time.Advance(IdleInterval - Instant);

    heartbeat.RecordActivity();
    _time.Advance(Instant);

    Assert.Equal(HeartbeatStatus.Alive, heartbeat.Check(out _));
  }

  [Fact]
  public void RecordActivity_WithAPingPending_DoesNotAnswerIt()
  {
    var heartbeat = CreateWithPendingPing();

    // Solo el Pong que corresponde confirma la vida (08 §6); otro mensaje válido no lo reemplaza.
    heartbeat.RecordActivity();
    _time.Advance(PongTimeout);

    Assert.Equal(HeartbeatStatus.TimedOut, heartbeat.Check(out _));
  }
}
