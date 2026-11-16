using Rts.Combat;
using Rts.Combat.Model;
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

public class AttackCommandHandlerTests
{
  private static readonly PositionXZ SoldierPosition = new(15.5, 15.5);
  private static readonly PositionXZ Waypoint = new(16.5, 16.5);
  private static readonly EquatableArray<PositionXZ> SomeRoute = [Waypoint];

  private static CommandEnvelope Attack(EntityId target, params EntityId[] units) =>
      new(CommandSequence.First, PlayerId.One, new ClientCommandId(1), new AttackCommand(
          target, MapRevision.Initial, [.. units.Select(unit => new UnitRouteCandidate(unit, SomeRoute))]));

  private static AttackCommandHandler Handler(FakeCombatRules? combat = null, FakeMovementRules? movement = null) =>
      new(combat ?? new FakeCombatRules(), movement ?? new FakeMovementRules());

  /// <summary>El mundo de prueba con un soldado de P1 (quieto, o atacando a <paramref name="attacking"/>).</summary>
  private static (TickContext Context, EntityId SoldierId) WithP1Soldier(EntityId? attacking = null)
  {
    (WorldState world, EntityId soldierId) = TestWorld.WithSoldier(TestWorld.Create(), PlayerId.One, SoldierPosition, attacking);
    return (new TickContext(world), soldierId);
  }

  [Fact]
  public void Handle_SoldierOnEnemyUnit_GetsAttackOrderNewOrderIdAndRoute()
  {
    (TickContext context, EntityId soldierId) = WithP1Soldier();
    OrderId expectedId = context.World.LastOrderId.Next();

    CommandResult result = Handler().Handle(Attack(TestWorld.P2Worker, soldierId), context);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    UnitEntity soldier = context.World.Units[soldierId];
    Assert.Equal(MovementOrder.Task(expectedId, UnitOrderKind.Attack, TestWorld.P2Worker), soldier.Order);
    Assert.Equal(SomeRoute, soldier.Route);
    Assert.Equal(expectedId, context.World.LastOrderId);
    Assert.Equal(new UnitOrderChanged(soldierId, expectedId, UnitOrderKind.Attack), Assert.Single(context.Events).Payload);
  }

  [Fact]
  public void Handle_AsksCombatWithTheAttackerAndTheTargetData()
  {
    var combat = new FakeCombatRules();
    (TickContext context, EntityId soldierId) = WithP1Soldier();

    Handler(combat).Handle(Attack(TestWorld.P2Worker, soldierId), context);

    Assert.Equal(
        new AttackValidationCall(PlayerId.One, UnitType.Soldier, new AttackTargetInfo(TestWorld.P2Worker, EntityKind.Unit, PlayerId.Two)),
        Assert.Single(combat.AttackValidations));
  }

  [Fact]
  public void Handle_UnitTarget_MovementGetsItsPoint()
  {
    var movement = new FakeMovementRules();
    (TickContext context, EntityId soldierId) = WithP1Soldier();
    PositionXZ targetPosition = context.World.Units[TestWorld.P2Worker].Position;

    Handler(movement: movement).Handle(Attack(TestWorld.P2Worker, soldierId), context);

    var request = Assert.Single(movement.Requests);
    Assert.Equal(UnitOrderKind.Attack, request.Kind);
    Assert.Equal(MovementTarget.AtPoint(TestWorld.P2Worker, targetPosition), request.Target);
    Assert.Same(context.Navigation, request.Map);
  }

  [Fact]
  public void Handle_BuildingTarget_MovementGetsItsFootprint()
  {
    var movement = new FakeMovementRules();
    (TickContext context, EntityId soldierId) = WithP1Soldier();
    Footprint centerFootprint = context.World.Buildings[TestWorld.P2Center].Footprint;

    Handler(movement: movement).Handle(Attack(TestWorld.P2Center, soldierId), context);

    Assert.Equal(MovementTarget.WithFootprint(TestWorld.P2Center, centerFootprint), Assert.Single(movement.Requests).Target);
  }

  [Fact]
  public void Handle_WorkerInTheGroup_IsRejectedByCombatAndMovementIsNotAsked()
  {
    var movement = new FakeMovementRules();
    (TickContext context, EntityId soldierId) = WithP1Soldier();

    CommandResult result = Handler(movement: movement).Handle(Attack(TestWorld.P2Worker, TestWorld.P1WorkerA, soldierId), context);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.Equal(
        [EntityCommandResult.Rejected(TestWorld.P1WorkerA, CommonErrors.IncompatibleEntity), EntityCommandResult.Accepted(soldierId)],
        result.EntityResults);
    Assert.Equal(soldierId, Assert.Single(movement.Requests).Unit.UnitId);
    Assert.True(context.World.Units[TestWorld.P1WorkerA].Order.IsIdle);
  }

  [Theory]
  [InlineData(nameof(TestWorld.P1WorkerA), "Combat.FriendlyTarget")]
  [InlineData(nameof(TestWorld.P1Center), "Combat.FriendlyTarget")]
  [InlineData(nameof(TestWorld.Tree), "Combat.InvalidTarget")]
  [InlineData(nameof(TestWorld.Missing), "Common.EntityNotFound")]
  public void Handle_InvalidTarget_RejectsTheWholeCommandAndLeavesTheWorldUntouched(string targetName, string expectedError)
  {
    EntityId target = (EntityId)typeof(TestWorld).GetField(targetName)!.GetValue(null)!;
    (TickContext context, EntityId soldierId) = WithP1Soldier();
    WorldState before = context.World;

    CommandResult result = Handler().Handle(Attack(target, soldierId), context);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(CommonErrors.NoEntityAccepted, result.Error);
    Assert.Equal(new ErrorCode(expectedError), Assert.Single(result.EntityResults).Error);
    Assert.Same(before, context.World);
    Assert.Empty(context.Events);
  }

  [Fact]
  public void Handle_MissingTarget_CombatReceivesNull()
  {
    var combat = new FakeCombatRules();
    (TickContext context, EntityId soldierId) = WithP1Soldier();

    Handler(combat).Handle(Attack(TestWorld.Missing, soldierId), context);

    Assert.Null(Assert.Single(combat.AttackValidations).Target);
  }

  [Fact]
  public void Handle_RivalUnit_IsRejectedByOwnershipBeforeAskingCombat()
  {
    var combat = new FakeCombatRules();
    (TickContext context, _) = WithP1Soldier();

    CommandResult result = Handler(combat).Handle(Attack(TestWorld.P1WorkerA, TestWorld.P2Worker), context);

    Assert.Equal(CommonErrors.NotOwner, Assert.Single(result.EntityResults).Error);
    Assert.Empty(combat.AttackValidations);
  }

  [Fact]
  public void Handle_RouteRejectedByMovement_SoldierKeepsItsPreviousAttack()
  {
    var movement = new FakeMovementRules();
    (TickContext context, EntityId soldierId) = WithP1Soldier(attacking: TestWorld.P2Center);
    movement.Rejections[soldierId] = MovementErrors.RouteDoesNotReachGoal;
    MovementOrder previous = context.World.Units[soldierId].Order;

    CommandResult result = Handler(movement: movement).Handle(Attack(TestWorld.P2Worker, soldierId), context);

    Assert.Equal(MovementErrors.RouteDoesNotReachGoal, Assert.Single(result.EntityResults).Error);
    Assert.Equal(previous, context.World.Units[soldierId].Order);
  }

  [Fact]
  public void Handle_CombatAcceptsAMissingTarget_IsACriticalFailure()
  {
    (TickContext context, EntityId soldierId) = WithP1Soldier();

    Assert.Throws<InvalidOperationException>(
        () => Handler(new FakeCombatRules { AcceptEverything = true }).Handle(Attack(TestWorld.Missing, soldierId), context));
  }
}
