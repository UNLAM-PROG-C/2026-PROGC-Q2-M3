using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Resources;
using Rts.Shared.Session;
using Rts.Shared.Time;
using Rts.Shared.World;

namespace Rts.Shared.Tests;

public class IntegrationTypesTests
{
    [Fact]
    public void MatchResult_FactoriesProduceValidCombinations()
    {
        MatchResult victory = MatchResult.BaseDestroyed(PlayerId.Two, new TickId(500));
        MatchResult draw = MatchResult.Draw(new TickId(500));
        MatchResult technical = MatchResult.CriticalError(null);

        Assert.Equal(PlayerId.Two, victory.Winner);
        Assert.Null(draw.Winner);
        Assert.Equal(MatchResultType.TechnicalEnd, technical.ResultType);
    }

    [Theory]
    [InlineData(MatchResultType.Victory, null, MatchEndReason.BaseDestroyed)]
    [InlineData(MatchResultType.Victory, 1, MatchEndReason.BothBasesDestroyed)]
    [InlineData(MatchResultType.Draw, 1, MatchEndReason.BothBasesDestroyed)]
    [InlineData(MatchResultType.Draw, null, MatchEndReason.PlayerDisconnected)]
    [InlineData(MatchResultType.TechnicalEnd, 2, MatchEndReason.CriticalError)]
    [InlineData(MatchResultType.TechnicalEnd, null, MatchEndReason.BaseDestroyed)]
    public void MatchResult_RejectsIncoherentCombinations(MatchResultType type, int? winner, MatchEndReason reason)
    {
        PlayerId? winnerId = winner is { } w ? new PlayerId(w) : null;

        Assert.Throws<ArgumentException>(() => new MatchResult(type, winnerId, reason, null));
    }

    [Fact]
    public void MatchResult_DisconnectionVictoryIsValid()
    {
        var result = new MatchResult(MatchResultType.Victory, PlayerId.One, MatchEndReason.PlayerDisconnected, new TickId(80));

        Assert.Equal(MatchEndReason.PlayerDisconnected, result.Reason);
        Assert.Equal(result, MatchResult.PlayerDisconnected(PlayerId.One, new TickId(80)));
    }

    [Fact]
    public void MatchResult_BothDisconnectedIsTechnicalWithoutWinner()
    {
        MatchResult result = MatchResult.BothPlayersDisconnected(null);

        Assert.Equal(MatchResultType.TechnicalEnd, result.ResultType);
        Assert.Null(result.Winner);
        Assert.Equal(MatchEndReason.BothPlayersDisconnected, result.Reason);
    }

    [Fact]
    public void GameCatalog_RequiresOneEntryPerType()
    {
        UnitCatalogEntry worker = new(UnitType.Worker, 40, new ResourceAmount(0, 50), 200, 3.0, null);
        UnitCatalogEntry soldier = new(UnitType.Soldier, 80, new ResourceAmount(0, 80), 300, 3.5, new AttackStatsView(8, 1.5, 20));
        EquatableArray<BuildingCatalogEntry> buildings =
        [
            new(BuildingType.MainCenter, 1000, new FootprintSize(4, 4), null, null),
            new(BuildingType.Farm, 200, new FootprintSize(2, 2), new ResourceAmount(60, 0), null),
            new(BuildingType.Barracks, 400, new FootprintSize(3, 3), new ResourceAmount(150, 0), null),
            new(BuildingType.Tower, 300, new FootprintSize(2, 2), new ResourceAmount(120, 0), new AttackStatsView(10, 6.0, 30)),
            new(BuildingType.Fence, 100, new FootprintSize(1, 1), new ResourceAmount(5, 0), null),
        ];

        var catalog = new GameCatalog(TickRate.Default, [worker, soldier], buildings, new ResourceAmount(30, 0), 100, 1.5, 1.5);

        Assert.Equal(2, catalog.Units.Count);
        Assert.Throws<ArgumentException>(() => new GameCatalog(TickRate.Default, [worker], buildings, new ResourceAmount(30, 0), 100, 1.5, 1.5));
        Assert.Throws<ArgumentException>(() => new GameCatalog(TickRate.Default, [worker, worker], buildings, new ResourceAmount(30, 0), 100, 1.5, 1.5));
    }

    [Fact]
    public void PrivateEvents_CarryTheirOwner()
    {
        // D-063: State Sync dirige los eventos privados sin mantener estado.
        Assert.Throws<ArgumentException>(() =>
            new RepathRequested(new EntityId(1), new PlayerId(0), new OrderId(3), MapRevision.Initial, null, new EntityId(9), isPaused: false));
        Assert.Equal(PlayerId.Two, new ProductionChanged(new EntityId(4), PlayerId.Two, ProducerStatus.Idle, null, null, []).Owner);
    }

    [Fact]
    public void SessionMessages_HaveValueEquality()
    {
        var session = new SessionId(Guid.Parse("00000000-0000-0000-0000-000000000003"));

        Assert.Equal(new MatchEnded(session, MatchResult.Draw(new TickId(9))), new MatchEnded(session, MatchResult.Draw(new TickId(9))));
        Assert.NotEqual(
            new StartAborted(InitializationId.First, StartAbortReason.OpponentLeft),
            new StartAborted(InitializationId.First, StartAbortReason.ReadyTimeout));
    }

    [Fact]
    public void CommandAdmission_AdmittedOrRejected()
    {
        CommandAdmission admitted = CommandAdmission.Admitted(new CommandSequence(12));
        CommandAdmission rejected = CommandAdmission.Rejected(CommonErrors.CommandQueueFull);

        Assert.True(admitted.IsAdmitted);
        Assert.Equal(new CommandSequence(12), admitted.Sequence);
        Assert.False(rejected.IsAdmitted);
        Assert.Null(rejected.Sequence);
        Assert.Throws<ArgumentException>(() => CommandAdmission.Rejected(default(ErrorCode)));
    }
}
