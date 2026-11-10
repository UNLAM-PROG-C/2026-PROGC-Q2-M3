using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Construction.Tests;

public class ContractsTests
{
    private static readonly GridSpec Grid = new(1.0, 30, 30);
    private static readonly Footprint TowerSite = new(new GridCell(5, 5), new FootprintSize(2, 2));

    [Fact]
    public void Occupancy_RejectsCellsOutsideMap()
    {
        Assert.Throws<ArgumentException>(() => new OccupancyView(Grid, [new GridCell(30, 0)], [], []));
        Assert.Throws<ArgumentException>(() => new OccupancyView(Grid, [], [new GridCell(-1, 0)], []));
    }

    [Fact]
    public void Occupancy_WithStructureAddsFootprintWithoutMutatingOriginal()
    {
        var view = new OccupancyView(Grid, [], [new GridCell(1, 1)], [new GridCell(2, 2)]);

        OccupancyView updated = view.WithStructure(TowerSite);

        Assert.Contains(new GridCell(6, 6), updated.StructureCells);
        Assert.Empty(view.StructureCells);
        Assert.Equal(view.TreeReservedCells, updated.TreeReservedCells);
        Assert.Equal(view.UnitCells, updated.UnitCells);
    }

    [Fact]
    public void BuildingUnderConstruction_MainCenterIsNeverUnderConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new BuildingUnderConstruction(new EntityId(1), PlayerId.One, BuildingType.MainCenter, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BuildingUnderConstruction(new EntityId(1), PlayerId.One, BuildingType.Farm, 0));
    }

    [Fact]
    public void TickInput_ValidatesTargetsAndWorkerUniqueness()
    {
        var farmInProgress = new BuildingUnderConstruction(new EntityId(10), PlayerId.One, BuildingType.Farm, 20);
        var repair = new FarmRepairState(new EntityId(20), PlayerId.One, 50);

        _ = new ConstructionTickInput(
            TickId.Zero, [farmInProgress], [new WorkerContribution(new EntityId(1), farmInProgress.BuildingId)],
            [repair], [new WorkerContribution(new EntityId(2), repair.FarmId)]);

        Assert.Throws<ArgumentException>(() => new ConstructionTickInput(
            TickId.Zero, [farmInProgress], [new WorkerContribution(new EntityId(1), new EntityId(99))], default, default));

        Assert.Throws<ArgumentException>(() => new ConstructionTickInput(
            TickId.Zero, [farmInProgress], [new WorkerContribution(new EntityId(1), farmInProgress.BuildingId)],
            [repair], [new WorkerContribution(new EntityId(1), repair.FarmId)]));

        Assert.Throws<ArgumentException>(() => new ConstructionTickInput(
            TickId.Zero, [farmInProgress, farmInProgress], default, default, default));
    }

    [Fact]
    public void PlacementInput_RequiresUniqueWorkers()
    {
        var site = new OccupancyView(Grid, [], [], []);

        Assert.Throws<ArgumentException>(() => new PlacementInput(PlayerId.One, BuildingType.Tower, TowerSite.Anchor, site, default));
        Assert.Throws<ArgumentException>(() => new PlacementInput(
            PlayerId.One, BuildingType.Tower, TowerSite.Anchor, site,
            [new WorkerPlacementCandidate(new EntityId(1), null), new WorkerPlacementCandidate(new EntityId(1), null)]));
    }

    [Fact]
    public void PlacementEvaluation_AcceptedNeedsAtLeastOneAcceptedWorker()
    {
        PlacementEvaluation accepted = PlacementEvaluation.Accepted(TowerSite, 30,
        [
            EntityCommandResult.Accepted(new EntityId(1)),
            EntityCommandResult.Rejected(new EntityId(2), new("Movement.InvalidPath")),
        ]);

        Assert.True(accepted.IsAccepted);
        Assert.Equal([new EntityId(1)], accepted.AcceptedWorkers);
        Assert.Throws<ArgumentException>(() => PlacementEvaluation.Accepted(TowerSite, 30,
            [EntityCommandResult.Rejected(new EntityId(2), new("Movement.InvalidPath"))]));
    }

    [Fact]
    public void PlacementEvaluation_RejectedAcceptsNoWorkers()
    {
        PlacementEvaluation rejected = PlacementEvaluation.Rejected(ConstructionErrors.NoReachableWorker,
            [EntityCommandResult.Rejected(new EntityId(2), new("Movement.Unreachable"))]);

        Assert.False(rejected.IsAccepted);
        Assert.Equal(0, rejected.InitialHp);
        Assert.Throws<ArgumentException>(() => PlacementEvaluation.Rejected(ConstructionErrors.CellOccupied,
            [EntityCommandResult.Accepted(new EntityId(1))]));
    }

    [Fact]
    public void SiteAndRepairEvaluations_RequireErrorWhenInvalid()
    {
        Assert.True(SiteEvaluation.Valid(TowerSite).IsValid);
        Assert.False(SiteEvaluation.Invalid(ConstructionErrors.TreeReserved).IsValid);
        Assert.Throws<ArgumentException>(() => SiteEvaluation.Invalid(default));

        Assert.Equal(RepairStartKind.Join, RepairStartEvaluation.Valid(RepairStartKind.Join).Kind);
        Assert.Throws<ArgumentException>(() => RepairStartEvaluation.Invalid(default));
    }

    [Fact]
    public void TickResult_EqualContentIsEqual()
    {
        ConstructionTickResult Build() => ConstructionTickResult.Empty(new TickId(4)) with
        {
            BuildingProgress = [new BuildingProgress(new EntityId(10), 200, IsCompleted: true)],
            RepairProgress = [new RepairProgress(new EntityId(20), 60, IsCompleted: false)],
            ProposedEvents = [new BuildingCompleted(new EntityId(10)), new FarmRepairProgressChanged(new EntityId(20), 60, 300)],
        };

        Assert.Equal(Build(), Build());
    }

    [Fact]
    public void ConstructionErrors_UseConstructionArea()
    {
        Assert.All(
            [ConstructionErrors.NotBuildable, ConstructionErrors.OutOfBounds, ConstructionErrors.CellOccupied,
             ConstructionErrors.TreeReserved, ConstructionErrors.UnitInFootprint, ConstructionErrors.NoReachableWorker,
             ConstructionErrors.AlreadyCompleted, ConstructionErrors.NotAFarm, ConstructionErrors.FarmNotDepleted],
            code => Assert.Equal("Construction", code.Area));
    }
}
