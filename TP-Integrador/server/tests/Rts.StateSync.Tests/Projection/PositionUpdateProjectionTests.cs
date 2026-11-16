using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.State;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;
using Rts.StateSync.Projection;

namespace Rts.StateSync.Tests.Projection;

public class PositionUpdateProjectionTests
{
  private const int FirstTick = 1;
  private const int TenHz = 10;
  private const int Millimeters = 3;

  private static readonly StateSyncConfig Config = new();

  /// <summary>Primer tick con posiciones según la configuración por defecto (hoy, 2).</summary>
  private static readonly TickId PositionTick = new(Config.GetPositionIntervalTicks(TickRate.Default));
  private static readonly EntityId Unit = new(3);

  [Fact]
  public void ProjectTick_OneSecondOfTicksGivesPositionRateHzUpdates()
  {
    var config = new StateSyncConfig();
    var projector = new StateProjector(config, TickRate.Default);

    int updates = Enumerable.Range(FirstTick, TickRate.Default.TicksPerSecond)
        .Count(tick => projector.ProjectTick(Publication(new TickId(tick))).Player1.Messages.Any(IsPositionUpdate));

    Assert.Equal(config.PositionRateHz, updates);
  }

  [Fact]
  public void ProjectTick_ThirtyTicksPerSecondPublishesEveryThreeTicks()
  {
    var thirtyHz = new TickRate(30);
    var projector = new StateProjector(new StateSyncConfig { PositionRateHz = TenHz }, thirtyHz);

    long[] ticks = [.. Enumerable.Range(FirstTick, thirtyHz.TicksPerSecond)
        .Select(tick => projector.ProjectTick(Publication(new TickId(tick))))
        .Where(p => p.Player1.Messages.Any(IsPositionUpdate))
        .Select(p => p.Tick.Value)];

    Assert.Equal([3, 6, 9, 12, 15, 18, 21, 24, 27, 30], ticks);
  }

  [Fact]
  public void ProjectTick_PositionUpdateIsSupersedableRoundedAndEqualForBoth()
  {
    var projector = new StateProjector(Config with { PositionDecimals = Millimeters }, TickRate.Default);

    TickProjection projection = projector.ProjectTick(Publication(PositionTick, new PositionXZ(1.23456, 7.0004)));

    foreach (RecipientMessages recipient in new[] { projection.Player1, projection.Player2 })
    {
      OutboundMessage message = Assert.Single(recipient.Messages);
      Assert.Equal(ProtocolMessageTypes.PositionUpdate, message.Type);
      Assert.Equal(DeliveryClass.Supersedable, message.Delivery);
      PositionUpdateDto dto = Assert.IsType<PositionUpdateDto>(message.Payload);
      Assert.Equal(PositionTick.Value, dto.Tick);
      Assert.Equal([new UnitPositionDto(Unit.Value, 1.235, 7.0)], dto.Units);
    }
  }

  [Fact]
  public void ProjectTick_StateEventsGoBeforePositions()
  {
    DomainEvent orderChanged = new(new EventStamp(PositionTick, 0), new UnitOrderChanged(Unit, new OrderId(1), UnitOrderKind.Move));

    TickProjection projection = Default().ProjectTick(Publication(PositionTick, events: [orderChanged]));

    Assert.Equal(
        [ProtocolMessageTypes.StateEvents, ProtocolMessageTypes.PositionUpdate],
        projection.Player1.Messages.Select(m => m.Type));
  }

  private static StateProjector Default() => new(Config, TickRate.Default);

  private static bool IsPositionUpdate(OutboundMessage message) => message.Type == ProtocolMessageTypes.PositionUpdate;

  /// <summary>Publicación con una sola unidad viva, en la posición dada (o en el origen).</summary>
  private static TickPublication Publication(TickId tick, PositionXZ position = default, EquatableArray<DomainEvent> events = default) =>
      new(tick, MapRevision.Initial, events, default, [new UnitPosition(Unit, position)], null);
}
