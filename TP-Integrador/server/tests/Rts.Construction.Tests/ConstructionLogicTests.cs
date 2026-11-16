using Rts.Construction.Configuration;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Construction.Tests;

public class ConstructionLogicTests
{
  private static readonly GridSpec Grid = new(1.0, 20, 20);

  private static ConstructionConfig ValidConfig() => new()
  {
    BuildingMaxHp = new Dictionary<BuildingType, int>
    {
      [BuildingType.MainCenter] = 1000,
      [BuildingType.Farm] = 200,
      [BuildingType.Barracks] = 300,
      [BuildingType.Tower] = 250,
      [BuildingType.Fence] = 50,
    },
    FootprintSizes = new Dictionary<BuildingType, FootprintSize>
    {
      [BuildingType.MainCenter] = new(3, 3),
      [BuildingType.Farm] = new(2, 2),
      [BuildingType.Barracks] = new(3, 2),
      [BuildingType.Tower] = new(2, 2),
      [BuildingType.Fence] = new(1, 1),
    },
    InitialHpFraction = 0.10,
    BuildHpPerWorkerTick = 10,
    RepairWorkRequired = 100,
    RepairWorkPerWorkerTick = 25,
  };

  private static ConstructionRules CreateRules() => new(ValidConfig());

  private static ConstructionCalculator CreateCalculator() => new(ValidConfig());

  [Fact]
  public void GetInitialHp_UsesCeilingAndClampsToValidRange()
  {
    ConstructionRules rules = CreateRules();

    Assert.Equal(20, rules.GetInitialHp(BuildingType.Farm));
    Assert.Equal(5, rules.GetInitialHp(BuildingType.Fence));
  }

  [Fact]
  public void EvaluateSite_RejectsMainCenterOutOfBoundsStructureTreeAndUnit()
  {
    ConstructionRules rules = CreateRules();
    var view = new OccupancyView(
        Grid,
        structureCells: [new GridCell(2, 2)],
        treeReservedCells: [new GridCell(5, 5)],
        unitCells: [new GridCell(8, 8)]);

    Assert.Equal(
        ConstructionErrors.NotBuildable,
        rules.EvaluateSite(BuildingType.MainCenter, new GridCell(0, 0), view).Error);
    Assert.Equal(
        ConstructionErrors.OutOfBounds,
        rules.EvaluateSite(BuildingType.Tower, new GridCell(19, 19), view).Error);
    Assert.Equal(
        ConstructionErrors.CellOccupied,
        rules.EvaluateSite(BuildingType.Tower, new GridCell(2, 2), view).Error);
    Assert.Equal(
        ConstructionErrors.TreeReserved,
        rules.EvaluateSite(BuildingType.Tower, new GridCell(4, 4), view).Error);
    Assert.Equal(
        ConstructionErrors.UnitInFootprint,
        rules.EvaluateSite(BuildingType.Tower, new GridCell(7, 7), view).Error);
  }

  [Fact]
  public void EvaluatePlacement_PartialAndTotalWorkerRejection()
  {
    ConstructionRules rules = CreateRules();
    var view = new OccupancyView(Grid, [], [], []);
    var anchor = new GridCell(2, 2);

    PlacementEvaluation partial = rules.EvaluatePlacement(new PlacementInput(
        PlayerId.One,
        BuildingType.Farm,
        anchor,
        view,
        [
          new WorkerPlacementCandidate(new EntityId(1), null),
          new WorkerPlacementCandidate(new EntityId(2), new ErrorCode("Movement.InvalidPath")),
        ]));

    PlacementEvaluation allRejected = rules.EvaluatePlacement(new PlacementInput(
        PlayerId.One,
        BuildingType.Farm,
        anchor,
        view,
        [new WorkerPlacementCandidate(new EntityId(1), new ErrorCode("Movement.Unreachable"))]));

    Assert.True(partial.IsAccepted);
    Assert.Equal(20, partial.InitialHp);
    Assert.Equal([new EntityId(1)], partial.AcceptedWorkers);
    Assert.False(allRejected.IsAccepted);
    Assert.Equal(ConstructionErrors.NoReachableWorker, allRejected.Error);
  }

  [Fact]
  public void EvaluateContinueBuild_ChecksExistenceOwnerAndCompletion()
  {
    ConstructionRules rules = CreateRules();
    var activeSite = new ConstructionTargetInfo(new EntityId(10), PlayerId.One, BuildingType.Tower, IsCompleted: false);
    var completedSite = activeSite with { IsCompleted = true };

    Assert.Equal(CommonErrors.EntityNotFound, rules.EvaluateContinueBuild(PlayerId.One, null));
    Assert.Equal(CommonErrors.NotOwner, rules.EvaluateContinueBuild(PlayerId.Two, activeSite));
    Assert.Equal(ConstructionErrors.AlreadyCompleted, rules.EvaluateContinueBuild(PlayerId.One, completedSite));
    Assert.Null(rules.EvaluateContinueBuild(PlayerId.One, activeSite));
  }

  [Fact]
  public void EvaluateRepairStart_DistinguishesErrorsStartAndPayAndJoin()
  {
    ConstructionRules rules = CreateRules();
    var depletedFarm = new RepairTargetInfo(
        new EntityId(10), PlayerId.One, BuildingType.Farm, IsCompleted: true, IsDepleted: true, IsRepairInProgress: false);

    Assert.Equal(CommonErrors.EntityNotFound, rules.EvaluateRepairStart(PlayerId.One, null).Error);
    Assert.Equal(CommonErrors.NotOwner, rules.EvaluateRepairStart(PlayerId.Two, depletedFarm).Error);
    Assert.Equal(
        ConstructionErrors.NotAFarm,
        rules.EvaluateRepairStart(PlayerId.One, depletedFarm with { BuildingType = BuildingType.Tower }).Error);
    Assert.Equal(
        ConstructionErrors.FarmNotDepleted,
        rules.EvaluateRepairStart(PlayerId.One, depletedFarm with { IsDepleted = false }).Error);
    Assert.Equal(
        RepairStartKind.StartAndPay,
        rules.EvaluateRepairStart(PlayerId.One, depletedFarm).Kind);
    Assert.Equal(
        RepairStartKind.Join,
        rules.EvaluateRepairStart(PlayerId.One, depletedFarm with { IsRepairInProgress = true }).Kind);
  }

  [Fact]
  public void CalculateTick_SumsWorkerContributionsAndCompletesBuildingAndRepair()
  {
    ConstructionCalculator calc = CreateCalculator();
    var farm = new BuildingUnderConstruction(new EntityId(10), PlayerId.One, BuildingType.Farm, hp: 185);
    var repair = new FarmRepairState(new EntityId(20), PlayerId.One, workDone: 60);

    var input = new ConstructionTickInput(
        new TickId(5),
        [farm],
        [new WorkerContribution(new EntityId(1), farm.BuildingId), new WorkerContribution(new EntityId(2), farm.BuildingId)],
        [repair],
        [new WorkerContribution(new EntityId(3), repair.FarmId), new WorkerContribution(new EntityId(4), repair.FarmId)]);

    ConstructionTickResult result = calc.CalculateTick(input);

    BuildingProgress bProg = Assert.Single(result.BuildingProgress);
    Assert.Equal(200, bProg.Hp);
    Assert.True(bProg.IsCompleted);

    RepairProgress rProg = Assert.Single(result.RepairProgress);
    Assert.Equal(110, rProg.WorkDone);
    Assert.True(rProg.IsCompleted);

    Assert.Collection(
        result.ProposedEvents,
        e => Assert.IsType<BuildingCompleted>(e),
        e => Assert.IsType<FarmRepairCompleted>(e));
  }

  [Fact]
  public void CalculateTick_PermutingInputsProducesIdenticalResult()
  {
    ConstructionCalculator calc = CreateCalculator();
    var b1 = new BuildingUnderConstruction(new EntityId(10), PlayerId.One, BuildingType.Farm, hp: 50);
    var b2 = new BuildingUnderConstruction(new EntityId(11), PlayerId.One, BuildingType.Tower, hp: 60);
    var w1 = new WorkerContribution(new EntityId(1), b1.BuildingId);
    var w2 = new WorkerContribution(new EntityId(2), b2.BuildingId);

    ConstructionTickResult r1 = calc.CalculateTick(new ConstructionTickInput(new TickId(1), [b1, b2], [w1, w2], default, default));
    ConstructionTickResult r2 = calc.CalculateTick(new ConstructionTickInput(new TickId(1), [b2, b1], [w2, w1], default, default));

    Assert.Equal(r1, r2);
  }
}
