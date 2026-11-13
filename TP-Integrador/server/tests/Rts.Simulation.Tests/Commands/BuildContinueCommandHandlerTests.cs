using Rts.Construction;
using Rts.Construction.Model;
using Rts.Movement;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

/// <summary>Forma "continuar" de <see cref="BuildCommandHandler"/> (03 §3, D-032).</summary>
public class BuildContinueCommandHandlerTests
{
  private const int SiteHp = 120;

  private static readonly GridCell SiteAnchor = new(20, 20);
  private static readonly PositionXZ Waypoint = new(19.5, 19.5);
  private static readonly EquatableArray<PositionXZ> SomeRoute = [Waypoint];

  private static CommandEnvelope Continue(EntityId site, params EntityId[] workers) =>
      new(CommandSequence.First, PlayerId.One, new ClientCommandId(1), BuildCommand.Continue(
          site, MapRevision.Initial, [.. workers.Select(worker => new UnitRouteCandidate(worker, SomeRoute))]));

  private static BuildCommandHandler Handler(FakeConstructionRules? construction = null, FakeMovementRules? movement = null) =>
      new(construction ?? new FakeConstructionRules(), new FakeEconomyRules(), movement ?? new FakeMovementRules());

  private static (TickContext Context, EntityId SiteId) WithSite(PlayerId owner)
  {
    (WorldState world, EntityId siteId) = TestWorld.WithSite(TestWorld.Create(), owner, BuildingType.Barracks, SiteAnchor, SiteHp);
    return (new TickContext(world), siteId);
  }

  [Fact]
  public void Handle_OwnSite_GivesTheBuildOrderAndKeepsTheProgress()
  {
    (TickContext context, EntityId siteId) = WithSite(PlayerId.One);
    OrderId first = context.World.LastOrderId.Next();
    OrderId second = first.Next();

    CommandResult result = Handler().Handle(Continue(siteId, TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.Equal(MovementOrder.Task(first, UnitOrderKind.Build, siteId), context.World.Units[TestWorld.P1WorkerA].Order);
    Assert.Equal(SiteHp, context.World.Buildings[siteId].Hp); // El progreso lo suma Construction en la fase 3.
    Assert.Same(context.Previous.Players, context.World.Players); // Sin costo.
    Assert.Equal(
        [
          new UnitOrderChanged(TestWorld.P1WorkerA, first, UnitOrderKind.Build),
          new UnitOrderChanged(TestWorld.P1WorkerB, second, UnitOrderKind.Build),
        ],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void Handle_AsksConstructionWithTheSiteDataAndMovementWithItsFootprint()
  {
    var construction = new FakeConstructionRules();
    var movement = new FakeMovementRules();
    (TickContext context, EntityId siteId) = WithSite(PlayerId.One);

    Handler(construction, movement).Handle(Continue(siteId, TestWorld.P1WorkerA), context);

    Assert.Equal(
        new ContinueBuildCall(PlayerId.One, new ConstructionTargetInfo(siteId, PlayerId.One, BuildingType.Barracks, IsCompleted: false)),
        Assert.Single(construction.ContinueBuilds));
    var request = Assert.Single(movement.Requests);
    Assert.Equal(UnitOrderKind.Build, request.Kind);
    Assert.Equal(MovementTarget.WithFootprint(siteId, context.World.Buildings[siteId].Footprint), request.Target);
  }

  [Fact]
  public void Handle_CompletedBuilding_IsRejectedWithoutAskingMovement()
  {
    var movement = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;

    CommandResult result = Handler(movement: movement).Handle(Continue(TestWorld.P1Center, TestWorld.P1WorkerA), context);

    Assert.Equal(ConstructionErrors.AlreadyCompleted, result.Error);
    Assert.True(result.EntityResults.IsEmpty);
    Assert.Empty(movement.Requests);
    Assert.Same(before, context.World);
  }

  [Fact]
  public void Handle_RivalSiteOrMissingTarget_IsRejected()
  {
    var construction = new FakeConstructionRules();
    (TickContext context, EntityId rivalSite) = WithSite(PlayerId.Two);

    CommandResult rival = Handler(construction).Handle(Continue(rivalSite, TestWorld.P1WorkerA), context);
    CommandResult missing = Handler(construction).Handle(Continue(TestWorld.Missing, TestWorld.P1WorkerA), context);

    Assert.Equal(CommonErrors.NotOwner, rival.Error);
    Assert.Equal(CommonErrors.EntityNotFound, missing.Error);
    Assert.Null(construction.ContinueBuilds[1].Target);
  }

  [Fact]
  public void Handle_MovementRejectsOneWorker_PartialResultAndThatWorkerKeepsItsOrder()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerB] = MovementErrors.RouteDoesNotReachGoal;
    (TickContext context, EntityId siteId) = WithSite(PlayerId.One);

    CommandResult result = Handler(movement: movement).Handle(Continue(siteId, TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.True(context.World.Units[TestWorld.P1WorkerB].Order.IsIdle);
  }

  [Fact]
  public void Handle_ConstructionAdmitsACompletedOrRivalBuilding_IsACriticalFailure()
  {
    var careless = new FakeConstructionRules { AcceptEveryContinue = true };
    (TickContext context, EntityId rivalSite) = WithSite(PlayerId.Two);

    Assert.Throws<InvalidOperationException>(() => Handler(careless).Handle(Continue(TestWorld.P1Center, TestWorld.P1WorkerA), context));
    Assert.Throws<InvalidOperationException>(() => Handler(careless).Handle(Continue(rivalSite, TestWorld.P1WorkerA), context));
  }
}
