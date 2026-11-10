using System.Collections.Immutable;
using Rts.Construction.Model;
using Rts.Economy.Model;
using Rts.Movement.Model;
using Rts.Production.Model;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Simulation.State;

namespace Rts.Simulation.Tests;

public class StateTests
{
    private static readonly GridSpec Grid = new(1.0, 40, 40);
    private static readonly Footprint CenterFootprint = new(new GridCell(2, 2), new FootprintSize(4, 4));

    private static UnitEntity Worker(int id, PlayerId owner) => new(
        new EntityId(id), owner, UnitType.Worker, new PositionXZ(10, 10), 40,
        MovementOrder.Idle, default, MapRevision.Initial, null, null, 0, TickId.Zero);

    private static BuildingEntity MainCenter(int id, PlayerId owner) => new(
        new EntityId(id), owner, BuildingType.MainCenter, CenterFootprint, 1000, TickId.Zero,
        null, null,
        new ProducerState(new EntityId(id), owner, BuildingType.MainCenter, CenterFootprint, TickId.Zero, null, default),
        null);

    private static ImmutableSortedDictionary<PlayerId, PlayerState> BothPlayers() =>
        ImmutableSortedDictionary.CreateRange(new Dictionary<PlayerId, PlayerState>
        {
            [PlayerId.One] = new(PlayerId.One, new ResourceAmount(100, 50)),
            [PlayerId.Two] = new(PlayerId.Two, new ResourceAmount(100, 50)),
        });

    private static WorldState World(
        ImmutableSortedDictionary<EntityId, UnitEntity>? units = null,
        ImmutableSortedDictionary<EntityId, BuildingEntity>? buildings = null,
        EntityId? lastEntityId = null,
        ImmutableSortedDictionary<PlayerId, PlayerState>? players = null) => new(
        TickId.Zero, Grid, MapRevision.Initial, players ?? BothPlayers(),
        units ?? ImmutableSortedDictionary<EntityId, UnitEntity>.Empty,
        buildings ?? ImmutableSortedDictionary<EntityId, BuildingEntity>.Empty,
        ImmutableSortedDictionary<EntityId, TreeEntity>.Empty,
        lastEntityId ?? new EntityId(100), OrderId.None, IdempotencyLedger.Empty, null);

    [Fact]
    public void World_RequiresBothPlayers()
    {
        var onlyP1 = ImmutableSortedDictionary.CreateRange(new Dictionary<PlayerId, PlayerState>
        {
            [PlayerId.One] = new(PlayerId.One, ResourceAmount.Zero),
        });

        Assert.Throws<ArgumentException>(() => World(players: onlyP1));
    }

    [Fact]
    public void World_IdsAreUniqueAcrossCollectionsAndBoundedByLastEntityId()
    {
        var units = ImmutableSortedDictionary<EntityId, UnitEntity>.Empty.Add(new EntityId(1), Worker(1, PlayerId.One));
        var buildingsClash = ImmutableSortedDictionary<EntityId, BuildingEntity>.Empty.Add(new EntityId(1), MainCenter(1, PlayerId.One));

        _ = World(units);
        Assert.Throws<ArgumentException>(() => World(units, buildingsClash));
        Assert.Throws<ArgumentException>(() => World(units, lastEntityId: EntityId.None));
    }

    [Fact]
    public void World_KeysMustMatchEntityIds()
    {
        var wrongKey = ImmutableSortedDictionary<EntityId, UnitEntity>.Empty.Add(new EntityId(2), Worker(1, PlayerId.One));

        Assert.Throws<ArgumentException>(() => World(wrongKey));
    }

    [Fact]
    public void Unit_InvariantsMatchMovementAndEconomyRules()
    {
        Assert.Throws<ArgumentException>(() => new UnitEntity(
            new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1, 1), 40,
            MovementOrder.Task(new OrderId(1), UnitOrderKind.Attack, new EntityId(9)), default, MapRevision.Initial, null, null, 0, TickId.Zero));

        Assert.Throws<ArgumentException>(() => new UnitEntity(
            new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1, 1), 40,
            MovementOrder.Move(new OrderId(1), new PositionXZ(5, 5)), default, MapRevision.Initial, null, null, 3, TickId.Zero));

        _ = new UnitEntity(
            new EntityId(1), PlayerId.One, UnitType.Worker, new PositionXZ(1, 1), 40,
            MovementOrder.Task(new OrderId(1), UnitOrderKind.GatherWood, new EntityId(9)), default, MapRevision.Initial, null, null, 3, TickId.Zero);
    }

    [Fact]
    public void Building_MainCenterIsAlwaysCompletedProducer()
    {
        _ = MainCenter(5, PlayerId.Two);

        Assert.Throws<ArgumentException>(() => new BuildingEntity(
            new EntityId(5), PlayerId.Two, BuildingType.MainCenter, CenterFootprint, 1000, null, null, null, null, null));
        Assert.Throws<ArgumentException>(() => new BuildingEntity(
            new EntityId(5), PlayerId.Two, BuildingType.MainCenter, CenterFootprint, 1000, TickId.Zero, null, null, null, null));
    }

    [Fact]
    public void Building_FarmPartsDependOnCompletionAndDepletion()
    {
        var footprint = new Footprint(new GridCell(10, 10), new FootprintSize(3, 3));
        var farmId = new EntityId(7);
        var producing = new FarmEconomyState(farmId, PlayerId.One, FarmProductionStatus.Producing, 5, 0, new TickId(4));
        var depleted = new FarmEconomyState(farmId, PlayerId.One, FarmProductionStatus.Depleted, 0, 0, new TickId(4));
        var repair = new FarmRepairState(farmId, PlayerId.One, 10);

        _ = new BuildingEntity(farmId, PlayerId.One, BuildingType.Farm, footprint, 50, null, null, null, null, null);
        _ = new BuildingEntity(farmId, PlayerId.One, BuildingType.Farm, footprint, 200, new TickId(4), depleted, repair, null, null);

        Assert.Throws<ArgumentException>(() =>
            new BuildingEntity(farmId, PlayerId.One, BuildingType.Farm, footprint, 50, null, producing, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            new BuildingEntity(farmId, PlayerId.One, BuildingType.Farm, footprint, 200, new TickId(4), null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            new BuildingEntity(farmId, PlayerId.One, BuildingType.Farm, footprint, 200, new TickId(4), producing, repair, null, null));
    }

    [Fact]
    public void Building_TowerCooldownOnlyWhenCompleted()
    {
        var footprint = new Footprint(new GridCell(10, 10), new FootprintSize(2, 2));

        _ = new BuildingEntity(new EntityId(8), PlayerId.One, BuildingType.Tower, footprint, 300, new TickId(9), null, null, null, new TickId(9));
        Assert.Throws<ArgumentException>(() =>
            new BuildingEntity(new EntityId(8), PlayerId.One, BuildingType.Tower, footprint, 30, null, null, null, null, new TickId(9)));
        Assert.Throws<ArgumentException>(() =>
            new BuildingEntity(new EntityId(8), PlayerId.One, BuildingType.Tower, footprint, 300, new TickId(9), null, null, null, null));
    }

    [Fact]
    public void Player_BalanceCannotBeNegative()
    {
        Assert.Throws<ArgumentException>(() => new PlayerState(PlayerId.One, new ResourceAmount(-1, 0)));
    }

    [Fact]
    public void Tree_EconomyMustShareId()
    {
        Assert.Throws<ArgumentException>(() =>
            new TreeEntity(new EntityId(3), new GridCell(1, 1), new PositionXZ(1.5, 1.5), TreeEconomyState.Available(new EntityId(4), 10)));
    }

    [Fact]
    public void Ledger_RecordsOncePerKey()
    {
        var envelope = new CommandEnvelope(new CommandSequence(1), PlayerId.One, new ClientCommandId(7), new TrainUnitCommand(new EntityId(5), UnitType.Worker));
        CommandResult result = CommandResult.Rejected(envelope, new TickId(1), CommonErrors.MatchNotRunning);

        IdempotencyLedger ledger = IdempotencyLedger.Empty.Record(result);

        Assert.Equal(0, IdempotencyLedger.Empty.Count);
        Assert.True(ledger.TryGet(envelope.Key, out CommandResult? stored));
        Assert.Same(result, stored);
        Assert.Throws<InvalidOperationException>(() => ledger.Record(result));
    }
}
