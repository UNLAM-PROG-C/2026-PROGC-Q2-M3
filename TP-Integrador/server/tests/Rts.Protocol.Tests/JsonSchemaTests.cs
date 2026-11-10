using System.Text.Json;
using Rts.Protocol.Outbound;
using Rts.Protocol.State;
using Rts.Shared.Commands;
using Rts.Shared.Entities;

namespace Rts.Protocol.Tests;

public class JsonSchemaTests
{
  private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, ProtocolJson.Options);

  private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, ProtocolJson.Options)!;

  [Fact]
  public void Conventions_CamelCaseEnumsAsStringsAndNullsOmitted()
  {
    var building = new BuildingDto(7, 1, BuildingType.Farm, new FootprintDto(3, 4, 2, 2), 80, IsCompleted: false, Farm: null);

    string json = Serialize(building);

    Assert.Equal(
        """{"buildingId":7,"owner":1,"buildingType":"Farm","footprint":{"column":3,"row":4,"width":2,"height":2},"hp":80,"isCompleted":false}""",
        json);
  }

  [Fact]
  public void Enums_RejectNumericValues()
  {
    Assert.Throws<JsonException>(() => Deserialize<UnitDto>("""{"unitId":1,"owner":1,"unitType":1,"x":0,"z":0,"hp":10,"order":"Idle"}"""));
  }

  [Fact]
  public void Events_CarryTypeDiscriminatorFirst()
  {
    StateEventDto spawned = new UnitSpawnedDto(Index: 3, UnitId: 42, Owner: 2, UnitType.Soldier, X: 10.5, Z: 4.25, Hp: 60);

    string json = Serialize(spawned);

    Assert.StartsWith("""{"type":"UnitSpawned",""", json);
    Assert.Contains("\"index\":3", json);
  }

  [Fact]
  public void StateEvents_RoundTripPreservesOrderAndTypes()
  {
    var batch = new StateEventsDto(
        Tick: 120,
        MapRevision: 4,
        Events:
        [
            new BuildingProgressChangedDto(0, 30, 55),
                new EntityDamagedDto(5, 30, 40),
                new RepathRequestedDto(6, 12, 9, 4, new PositionDto(1.5, 2.5), null, IsPaused: true),
                new ResourcesChangedDto(9, 120, 35),
        ],
        CommandResults: [new CommandResultDto(77, 501, 120, CommandOutcome.PartiallyAccepted, null, [new EntityResultDto(12, null), new EntityResultDto(13, "Movement.InvalidPath")])]);

    string json = Serialize(batch);
    StateEventsDto back = Deserialize<StateEventsDto>(json);

    Assert.Equal(json, Serialize(back));
    Assert.Collection(
        back.Events,
        e => Assert.IsType<BuildingProgressChangedDto>(e),
        e => Assert.Equal(40, Assert.IsType<EntityDamagedDto>(e).Hp),
        e => Assert.Null(Assert.IsType<RepathRequestedDto>(e).TargetId),
        e => Assert.IsType<ResourcesChangedDto>(e));
  }

  [Fact]
  public void Positions_AreCompact()
  {
    var update = new PositionUpdateDto(40, [new UnitPositionDto(5, 1.25, 7.5)]);

    Assert.Equal("""{"tick":40,"units":[{"id":5,"x":1.25,"z":7.5}]}""", Serialize(update));
  }

  [Fact]
  public void OutboundMessage_ValidatesItsParts()
  {
    var payload = new PositionUpdateDto(2, []);

    Assert.Throws<ArgumentException>(() => new OutboundMessage(" ", payload, DeliveryClass.Supersedable));
    Assert.Throws<ArgumentNullException>(() => new OutboundMessage(ProtocolMessageTypes.PositionUpdate, null!, DeliveryClass.Supersedable));
    Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundMessage(ProtocolMessageTypes.PositionUpdate, payload, (DeliveryClass)0));
  }
}
