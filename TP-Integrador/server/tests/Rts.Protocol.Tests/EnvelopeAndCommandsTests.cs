using System.Text.Json;
using Rts.Protocol.Commands;
using Rts.Protocol.Session;
using Rts.Protocol.State;
using Rts.Protocol.Transport;
using Rts.Shared.Entities;
using Rts.Shared.Match;

namespace Rts.Protocol.Tests;

public class EnvelopeAndCommandsTests
{
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, ProtocolJson.Options);

    [Fact]
    public void Envelope_ReadsGameplayLineWithPayloadKeptRaw()
    {
        const string line = """{"protocolVersion":1,"type":"TrainUnit","messageId":42,"payload":{"producerId":7,"unitType":"Worker"}}""";

        NetworkEnvelope envelope = JsonSerializer.Deserialize<NetworkEnvelope>(line, ProtocolJson.Options)!;
        TrainUnitCommandDto command = envelope.Payload!.Value.Deserialize<TrainUnitCommandDto>(ProtocolJson.Options)!;

        Assert.Equal(1, envelope.ProtocolVersion);
        Assert.Equal(ProtocolMessageTypes.TrainUnit, envelope.Type);
        Assert.Equal(42, envelope.MessageId);
        Assert.Equal(new TrainUnitCommandDto(7, UnitType.Worker), command);
    }

    [Fact]
    public void Envelope_OmitsMissingMessageIdAndPayload()
    {
        var envelope = new NetworkEnvelope(1, ProtocolMessageTypes.Hello, null, null);

        Assert.Equal("""{"protocolVersion":1,"type":"Hello"}""", Serialize(envelope));
    }

    [Fact]
    public void BuildCommand_PlaceAndContinueForms()
    {
        const string place = """{"buildingType":"Tower","anchorColumn":10,"anchorRow":12,"routeMapRevision":3,"routes":[{"unitId":5,"waypoints":[{"x":9.5,"z":11.5}]}]}""";
        const string resume = """{"buildingId":88,"routeMapRevision":3,"routes":[]}""";

        BuildCommandDto placed = JsonSerializer.Deserialize<BuildCommandDto>(place, ProtocolJson.Options)!;
        BuildCommandDto resumed = JsonSerializer.Deserialize<BuildCommandDto>(resume, ProtocolJson.Options)!;

        Assert.Equal(BuildingType.Tower, placed.BuildingType);
        Assert.Null(placed.BuildingId);
        Assert.Single(placed.Routes[0].Waypoints);
        Assert.Equal(88, resumed.BuildingId);
        Assert.Null(resumed.BuildingType);
    }

    [Fact]
    public void SessionAndTransportDtos_FollowConventions()
    {
        var draw = new MatchEndedDto("s-1", MatchResultType.Draw, null, MatchEndReason.BothBasesDestroyed, 500);
        var rejected = new CommandRejectedDto(42, "Common.MatchNotRunning");

        Assert.Equal("""{"sessionId":"s-1","resultType":"Draw","reason":"BothBasesDestroyed","finalTick":500}""", Serialize(draw));
        Assert.Equal("""{"messageId":42,"error":"Common.MatchNotRunning"}""", Serialize(rejected));
    }

    [Fact]
    public void RepathResponse_RoundTrips()
    {
        var dto = new RepathResponseCommandDto(12, 9, 4, [new PositionDto(1, 2), new PositionDto(3.5, 2)]);

        string json = Serialize(dto);

        Assert.Equal(json, Serialize(JsonSerializer.Deserialize<RepathResponseCommandDto>(json, ProtocolJson.Options)));
    }
}
