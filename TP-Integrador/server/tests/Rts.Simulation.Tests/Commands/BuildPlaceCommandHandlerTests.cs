using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Economy;
using Rts.Movement;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Commands;

/// <summary>Forma "colocar" de <see cref="BuildCommandHandler"/> (03 §3): <see cref="BuildPlacement"/>.</summary>
public class BuildPlaceCommandHandlerTests
{
  private const int NotEnoughWood = 10;

  private static readonly GridCell FreeAnchor = new(20, 10);
  private static readonly GridCell OtherFreeAnchor = new(25, 10);
  private static readonly Footprint FarmFootprint = new(FreeAnchor, FakeConstructionRules.FarmSize);
  private static readonly PositionXZ Waypoint = new(19.5, 9.5);
  private static readonly EquatableArray<PositionXZ> SomeRoute = [Waypoint];
  private static readonly CommandSequence SecondSequence = new(2);

  private static CommandEnvelope Place(GridCell anchor, BuildingType type, CommandSequence sequence, params EntityId[] workers) =>
      new(sequence, PlayerId.One, new ClientCommandId(sequence.Value), BuildCommand.Place(
          type, anchor, MapRevision.Initial, [.. workers.Select(worker => new UnitRouteCandidate(worker, SomeRoute))]));

  private static CommandEnvelope PlaceFarm(params EntityId[] workers) => Place(FreeAnchor, BuildingType.Farm, CommandSequence.First, workers);

  private static BuildCommandHandler Handler(FakeConstructionRules? construction = null, FakeMovementRules? movement = null) =>
      new(construction ?? new FakeConstructionRules(), new FakeEconomyRules(), movement ?? new FakeMovementRules());

  private static ResourceAmount BalanceOf(WorldState world, PlayerId player) => world.Players[player].Resources;

  [Fact]
  public void Handle_Accepted_PaysCreatesTheSiteAdvancesTheMapAndGivesTheOrders()
  {
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;
    EntityId siteId = before.LastEntityId.Next();
    OrderId first = before.LastOrderId.Next();
    OrderId second = first.Next();
    MapRevision revision = before.MapRevision.Next();

    CommandResult result = Handler().Handle(PlaceFarm(TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.Equal(BalanceOf(before, PlayerId.One) - FakeEconomyRules.FarmCost, BalanceOf(context.World, PlayerId.One));
    BuildingEntity site = context.World.Buildings[siteId];
    Assert.Equal((PlayerId.One, BuildingType.Farm, FarmFootprint), (site.Owner, site.Type, site.Footprint));
    Assert.Equal(FakeConstructionRules.TestInitialHp, site.Hp);
    Assert.False(site.IsCompleted);
    Assert.Equal(revision, context.World.MapRevision);
    Assert.Equal(siteId, context.World.LastEntityId);
    Assert.Equal(MovementOrder.Task(first, UnitOrderKind.Build, siteId), context.World.Units[TestWorld.P1WorkerA].Order);
    Assert.Equal(
        [
          new BuildingPlaced(siteId, PlayerId.One, BuildingType.Farm, FarmFootprint, FakeConstructionRules.TestInitialHp),
          new MapRevisionChanged(revision),
          new UnitOrderChanged(TestWorld.P1WorkerA, first, UnitOrderKind.Build),
          new UnitOrderChanged(TestWorld.P1WorkerB, second, UnitOrderKind.Build),
        ],
        context.Events.Select(stamped => stamped.Payload));
  }

  [Fact]
  public void Handle_MovementEvaluatesTheWorkersAgainstTheHypotheticalMap()
  {
    var movement = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    EntityId siteId = context.World.LastEntityId.Next();
    MapRevision before = context.World.MapRevision;

    Handler(movement: movement).Handle(PlaceFarm(TestWorld.P1WorkerA), context);

    var request = Assert.Single(movement.Requests);
    Assert.All(FarmFootprint.Cells(), cell => Assert.True(request.Map.IsBlocked(cell))); // La obra todavía no existía.
    Assert.Equal(before, request.Map.Revision); // El mapa hipotético conserva la revisión: la fase 2 revalida la ruta.
    Assert.Equal(MovementTarget.WithFootprint(siteId, FarmFootprint), request.Target);
  }

  [Theory]
  [InlineData(BuildingType.MainCenter, 20, 10, "Construction.NotBuildable")]
  [InlineData(BuildingType.Farm, 39, 39, "Construction.OutOfBounds")]
  [InlineData(BuildingType.Farm, 3, 3, "Construction.CellOccupied")] // Sobre el Centro Principal de P1.
  [InlineData(BuildingType.Farm, 9, 19, "Construction.TreeReserved")] // Incluye la celda del árbol (10, 20).
  [InlineData(BuildingType.Farm, 7, 3, "Construction.UnitInFootprint")] // Incluye la celda de un trabajador.
  public void Handle_InvalidSite_RejectsWithoutAskingMovementOrCharging(BuildingType type, int column, int row, string expectedError)
  {
    var movement = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;

    CommandResult result = Handler(movement: movement).Handle(
        Place(new GridCell(column, row), type, CommandSequence.First, TestWorld.P1WorkerA), context);

    Assert.Equal(new ErrorCode(expectedError), result.Error);
    Assert.True(result.EntityResults.IsEmpty);
    Assert.Empty(movement.Requests);
    Assert.Same(before, context.World);
  }

  [Fact]
  public void Handle_NoReachableWorker_RejectsEverythingWithoutCharging()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerA] = MovementErrors.Unreachable;
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;

    CommandResult result = Handler(movement: movement).Handle(PlaceFarm(TestWorld.P1WorkerA), context);

    Assert.Equal(ConstructionErrors.NoReachableWorker, result.Error);
    Assert.Equal(MovementErrors.Unreachable, Assert.Single(result.EntityResults).Error);
    Assert.Same(before, context.World);
    Assert.Empty(context.Events);
  }

  [Fact]
  public void Handle_OneOfTwoWorkersReaches_PaysOnceAndAcceptsPartially()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerB] = MovementErrors.InvalidPath;
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;

    CommandResult result = Handler(movement: movement).Handle(PlaceFarm(TestWorld.P1WorkerA, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.Equal(BalanceOf(before, PlayerId.One) - FakeEconomyRules.FarmCost, BalanceOf(context.World, PlayerId.One));
    Assert.True(context.World.Units[TestWorld.P1WorkerB].Order.IsIdle);
  }

  [Fact]
  public void Handle_RivalWorker_IsReportedAndDoesNotCount()
  {
    var context = new TickContext(TestWorld.Create());

    CommandResult result = Handler().Handle(PlaceFarm(TestWorld.P2Worker), context);

    Assert.Equal(ConstructionErrors.NoReachableWorker, result.Error);
    Assert.Equal(CommonErrors.NotOwner, Assert.Single(result.EntityResults).Error);
  }

  [Fact]
  public void Handle_NotEnoughWood_RejectsTheWholeCommandWithEconomysError()
  {
    WorldState world = TestWorld.Create();
    world = world.With(players: world.Players.SetItem(PlayerId.One, new PlayerState(PlayerId.One, ResourceAmount.OfWood(NotEnoughWood))));
    var context = new TickContext(world);
    WorldState before = context.World;

    CommandResult result = Handler().Handle(PlaceFarm(TestWorld.P1WorkerA), context);

    Assert.Equal(EconomyErrors.InsufficientResources, result.Error);
    Assert.Same(before, context.World);
    Assert.Empty(context.Events);
  }

  [Fact]
  public void Handle_TwoPlacementsOnTheSameCellInOneBatch_OnlyTheFirstIsAcceptedAndPaid()
  {
    var context = new TickContext(TestWorld.Create());
    WorldState before = context.World;
    BuildCommandHandler handler = Handler();

    CommandResult first = handler.Handle(Place(FreeAnchor, BuildingType.Farm, CommandSequence.First, TestWorld.P1WorkerA), context);
    CommandResult second = handler.Handle(Place(FreeAnchor, BuildingType.Farm, SecondSequence, TestWorld.P1WorkerB), context);

    Assert.Equal(CommandOutcome.Accepted, first.Outcome);
    Assert.Equal(ConstructionErrors.CellOccupied, second.Error); // La segunda ve la obra de la primera (AGENTS §7).
    Assert.Equal(BalanceOf(before, PlayerId.One) - FakeEconomyRules.FarmCost, BalanceOf(context.World, PlayerId.One));
    Assert.Single(context.World.Buildings.Values, building => building.Footprint == FarmFootprint);
  }

  [Fact]
  public void Handle_SecondPlacementInTheBatch_MovementSeesTheFirstSiteBlocked()
  {
    var movement = new FakeMovementRules();
    var context = new TickContext(TestWorld.Create());
    BuildCommandHandler handler = Handler(movement: movement);

    handler.Handle(Place(FreeAnchor, BuildingType.Farm, CommandSequence.First, TestWorld.P1WorkerA), context);
    MapRevision afterFirst = context.World.MapRevision;
    handler.Handle(Place(OtherFreeAnchor, BuildingType.Farm, SecondSequence, TestWorld.P1WorkerB), context);

    Assert.True(movement.Requests[^1].Map.IsBlocked(FreeAnchor));
    Assert.Equal(afterFirst, movement.Requests[^1].Map.Revision); // El mapa de navegación se rearmó tras la primera colocación.
  }

  [Fact]
  public void Handle_ConstructionAcceptsAWorkerMovementRejected_IsACriticalFailure()
  {
    var movement = new FakeMovementRules();
    movement.Rejections[TestWorld.P1WorkerA] = MovementErrors.InvalidPath;
    var careless = new FakeConstructionRules
    {
      ForcedPlacement = PlacementEvaluation.Accepted(
          FarmFootprint, FakeConstructionRules.TestInitialHp, [EntityCommandResult.Accepted(TestWorld.P1WorkerA)]),
    };

    Assert.Throws<InvalidOperationException>(
        () => Handler(careless, movement).Handle(PlaceFarm(TestWorld.P1WorkerA), new TickContext(TestWorld.Create())));
  }

  [Fact]
  public void Handle_ConstructionChangesTheFootprint_IsACriticalFailure()
  {
    var careless = new FakeConstructionRules
    {
      ForcedPlacement = PlacementEvaluation.Accepted(
          new Footprint(OtherFreeAnchor, FakeConstructionRules.FarmSize), FakeConstructionRules.TestInitialHp,
          [EntityCommandResult.Accepted(TestWorld.P1WorkerA)]),
    };

    Assert.Throws<InvalidOperationException>(
        () => Handler(careless).Handle(PlaceFarm(TestWorld.P1WorkerA), new TickContext(TestWorld.Create())));
  }
}
