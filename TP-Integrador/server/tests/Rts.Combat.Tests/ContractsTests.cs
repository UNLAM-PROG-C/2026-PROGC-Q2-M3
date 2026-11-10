using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Combat.Tests;

public class ContractsTests
{
    private static readonly TickId Tick = new(30);
    private static readonly GridSpec Grid = new(cellSize: 1.0, columns: 50, rows: 50);

    private static readonly CombatUnit SoldierP1 = new(new EntityId(1), PlayerId.One, UnitType.Soldier, new PositionXZ(5, 5), 100);
    private static readonly CombatUnit WorkerP2 = new(new EntityId(2), PlayerId.Two, UnitType.Worker, new PositionXZ(6, 5), 40);
    private static readonly CombatBuilding TowerP2 = new(
        new EntityId(10), PlayerId.Two, BuildingType.Tower, new Footprint(new GridCell(10, 10), new FootprintSize(1, 1)), 200, new TickId(5));
    private static readonly CombatBuilding BarracksInConstruction = new(
        new EntityId(11), PlayerId.Two, BuildingType.Barracks, new Footprint(new GridCell(20, 20), new FootprintSize(3, 3)), 30, null);

    [Fact]
    public void TickInput_AcceptsConsistentSnapshot()
    {
        var input = new CombatTickInput(
            Tick, Grid,
            [SoldierP1, WorkerP2],
            [TowerP2, BarracksInConstruction],
            [new SoldierAttacker(SoldierP1.Id, WorkerP2.Id, TickId.Zero)],
            [new TowerAttacker(TowerP2.Id, TickId.Zero)]);

        Assert.Single(input.Soldiers);
        Assert.Single(input.Towers);
    }

    [Fact]
    public void TickInput_IdsAreUniqueAcrossUnitsAndBuildings()
    {
        var buildingWithUnitId = new CombatBuilding(
            SoldierP1.Id, PlayerId.Two, BuildingType.Farm, new Footprint(new GridCell(0, 0), new FootprintSize(2, 2)), 50, null);

        Assert.Throws<ArgumentException>(() => new CombatTickInput(Tick, Grid, [SoldierP1], [buildingWithUnitId], default, default));
    }

    [Fact]
    public void TickInput_SoldierAttackerMustBeASoldierUnit()
    {
        Assert.Throws<ArgumentException>(() => new CombatTickInput(
            Tick, Grid, [SoldierP1, WorkerP2], default,
            [new SoldierAttacker(WorkerP2.Id, SoldierP1.Id, TickId.Zero)], default));

        Assert.Throws<ArgumentException>(() => new CombatTickInput(
            Tick, Grid, [WorkerP2], default,
            [new SoldierAttacker(new EntityId(99), WorkerP2.Id, TickId.Zero)], default));
    }

    [Fact]
    public void TickInput_SoldierTargetMayBeMissing_DefensiveCase()
    {
        var input = new CombatTickInput(
            Tick, Grid, [SoldierP1], default,
            [new SoldierAttacker(SoldierP1.Id, new EntityId(77), TickId.Zero)], default);

        Assert.Single(input.Soldiers);
    }

    [Fact]
    public void TickInput_TowerAttackerMustBeACompletedTower()
    {
        Assert.Throws<ArgumentException>(() => new CombatTickInput(
            Tick, Grid, default, [BarracksInConstruction],
            default, [new TowerAttacker(BarracksInConstruction.Id, TickId.Zero)]));

        var towerInConstruction = new CombatBuilding(
            new EntityId(12), PlayerId.Two, BuildingType.Tower, new Footprint(new GridCell(0, 0), new FootprintSize(1, 1)), 20, null);
        Assert.Throws<ArgumentException>(() => new CombatTickInput(
            Tick, Grid, default, [towerInConstruction],
            default, [new TowerAttacker(towerInConstruction.Id, TickId.Zero)]));
    }

    [Fact]
    public void Building_OperationalFromNextTickAfterCompletion()
    {
        var completedAtTick29 = new CombatBuilding(
            new EntityId(13), PlayerId.One, BuildingType.Tower, new Footprint(new GridCell(0, 0), new FootprintSize(1, 1)), 200, new TickId(30));

        Assert.True(completedAtTick29.IsCompleted);
        Assert.False(completedAtTick29.IsOperationalAt(new TickId(29)));
        Assert.True(completedAtTick29.IsOperationalAt(new TickId(30)));
        Assert.False(BarracksInConstruction.IsCompleted);
        Assert.False(BarracksInConstruction.IsOperationalAt(new TickId(1000)));
    }

    [Fact]
    public void Models_ValidateInvariants()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatUnit(new EntityId(3), PlayerId.One, UnitType.Worker, new PositionXZ(0, 0), 0));
        Assert.Throws<ArgumentException>(() =>
            new CombatUnit(new EntityId(3), PlayerId.One, UnitType.Worker, new PositionXZ(double.NaN, 0), 10));
        Assert.Throws<ArgumentException>(() =>
            new CombatUnit(new EntityId(3), new PlayerId(5), UnitType.Worker, new PositionXZ(0, 0), 10));
        Assert.Throws<ArgumentException>(() => new SoldierAttacker(new EntityId(1), EntityId.None, TickId.Zero));
        Assert.Throws<ArgumentException>(() => new SoldierAttacker(new EntityId(1), new EntityId(1), TickId.Zero));
    }

    [Theory]
    [InlineData(100, 30, 70, false)]
    [InlineData(100, 100, 0, true)]
    [InlineData(20, 35, 0, true)]
    public void TargetDamage_AppliesFloorZeroOnce(int hpBefore, int damage, int expectedHp, bool destroyed)
    {
        var target = new TargetDamage(new EntityId(5), hpBefore, damage);

        Assert.Equal(expectedHp, target.HpAfter);
        Assert.Equal(destroyed, target.IsDestroyed);
    }

    [Fact]
    public void TargetDamage_RequiresPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TargetDamage(new EntityId(5), 10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TargetDamage(new EntityId(5), 0, 10));
    }

    [Fact]
    public void TickResult_EqualContentIsEqual_ForDeterminismTests()
    {
        CombatTickResult Build() => CombatTickResult.Empty(Tick) with
        {
            Attacks = [new AttackPerformed(SoldierP1.Id, WorkerP2.Id, 10)],
            Damage = [new TargetDamage(WorkerP2.Id, 40, 10)],
            CooldownUpdates = [new CooldownUpdate(SoldierP1.Id, Tick.Plus(20))],
            ProposedEvents = [new EntityDamaged(WorkerP2.Id, 30)],
        };

        Assert.Equal(Build(), Build());
        Assert.NotEqual(Build(), CombatTickResult.Empty(Tick));
    }

    [Fact]
    public void CombatErrors_UseCombatArea()
    {
        Assert.Equal("Combat", CombatErrors.InvalidTarget.Area);
        Assert.Equal("Combat", CombatErrors.FriendlyTarget.Area);
    }

    [Fact]
    public void AttackTargetInfo_TreeHasNoOwner()
    {
        var tree = new AttackTargetInfo(new EntityId(500), EntityKind.Tree, null);

        Assert.Null(tree.Owner);
        Assert.Equal(EntityKind.Tree, tree.Kind);
    }
}
