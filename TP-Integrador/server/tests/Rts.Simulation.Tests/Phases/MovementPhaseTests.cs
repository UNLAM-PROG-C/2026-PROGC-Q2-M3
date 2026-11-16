using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.Phases;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Phases;

public class MovementPhaseTests
{
  private const int GatherTicksSoFar = 7;

  private static readonly PositionXZ NearbyPosition = new(8.5, 3.5);

  [Fact]
  public void Execute_SendsTheCurrentTickMapAndEveryUnit()
  {
    var calculator = new FakeMovementCalculator();
    var context = new TickContext(TestWorld.Create());

    new MovementPhase(calculator).Execute(context);

    MovementTickInput input = Assert.Single(calculator.Inputs);
    Assert.Equal(context.Tick, input.Tick);
    Assert.Same(context.Navigation, input.Map);
    Assert.Equal(context.World.Units.Keys, input.Units.Select(unit => unit.UnitId));
  }

  [Fact]
  public void Execute_CollectsEachTargetOnceWithItsGeometry()
  {
    WorldState world = TestWorld.WithTaskOrder(TestWorld.Create(), TestWorld.P1WorkerA, UnitOrderKind.GatherWood, TestWorld.Tree);
    world = TestWorld.WithTaskOrder(world, TestWorld.P1WorkerB, UnitOrderKind.GatherWood, TestWorld.Tree);
    world = TestWorld.WithTaskOrder(world, TestWorld.P2Worker, UnitOrderKind.Build, TestWorld.P2Center);
    var calculator = new FakeMovementCalculator();

    new MovementPhase(calculator).Execute(new TickContext(world));

    Assert.Equal(
        [
          MovementTarget.AtPoint(TestWorld.Tree, world.Trees[TestWorld.Tree].Position),
          MovementTarget.WithFootprint(TestWorld.P2Center, world.Buildings[TestWorld.P2Center].Footprint),
        ],
        calculator.Inputs[0].Targets);
  }

  [Fact]
  public void Execute_TargetThatNoLongerExists_IsLeftOut()
  {
    WorldState world = TestWorld.WithTaskOrder(TestWorld.Create(), TestWorld.P1WorkerA, UnitOrderKind.GatherWood, TestWorld.Missing);
    var calculator = new FakeMovementCalculator();

    new MovementPhase(calculator).Execute(new TickContext(world));

    Assert.Empty(calculator.Inputs[0].Targets);
  }

  [Fact]
  public void Execute_MovingUnitReachesItsLastWaypoint_BecomesIdleWithEvent()
  {
    var context = new TickContext(TestWorld.WithMovingUnit(TestWorld.Create(), TestWorld.P1WorkerA));
    UnitEntity untouchedBefore = context.World.Units[TestWorld.P1WorkerB];

    new MovementPhase(new FakeMovementCalculator()).Execute(context);

    UnitEntity unit = context.World.Units[TestWorld.P1WorkerA];
    Assert.Equal(TestWorld.SomeDestination, unit.Position);
    Assert.True(unit.Order.IsIdle);
    Assert.True(unit.Route.IsEmpty);
    DomainEvent changed = Assert.Single(context.Events);
    Assert.Equal(new UnitOrderChanged(TestWorld.P1WorkerA, OrderId.None, UnitOrderKind.Idle), changed.Payload);
    Assert.Same(untouchedBefore, context.World.Units[TestWorld.P1WorkerB]);
  }

  [Fact]
  public void Execute_SameOrderKeepsGatherCounterAndHp()
  {
    var context = new TickContext(TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, GatherTicksSoFar));
    UnitEntity before = context.World.Units[TestWorld.P1WorkerA];
    var calculator = new ScriptedCalculator(input => Result(input, MoveTo(input, TestWorld.P1WorkerA, NearbyPosition)));

    new MovementPhase(calculator).Execute(context);

    UnitEntity after = context.World.Units[TestWorld.P1WorkerA];
    Assert.Equal(NearbyPosition, after.Position);
    Assert.Equal(GatherTicksSoFar, after.GatherTicksAccumulated);
    Assert.Equal(before.Hp, after.Hp);
  }

  [Fact]
  public void Execute_EndedOrderDiscardsGatherCounter()
  {
    var context = new TickContext(TestWorld.WithGatheringUnit(TestWorld.Create(), TestWorld.P1WorkerA, GatherTicksSoFar));
    var calculator = new ScriptedCalculator(input => Result(input, AsIdle(input, TestWorld.P1WorkerA)));

    new MovementPhase(calculator).Execute(context);

    UnitEntity after = context.World.Units[TestWorld.P1WorkerA];
    Assert.True(after.Order.IsIdle);
    Assert.Equal(0, after.GatherTicksAccumulated);
  }

  [Fact]
  public void Execute_UpdateForUnknownUnit_Throws()
  {
    var ghost = new UnitMovementState(
        TestWorld.Missing, PlayerId.One, UnitType.Worker, NearbyPosition, MovementOrder.Idle, default, MapRevision.Initial);
    var calculator = new ScriptedCalculator(input => Result(input, ghost));

    Assert.Throws<InvalidOperationException>(() => new MovementPhase(calculator).Execute(new TickContext(TestWorld.Create())));
  }

  [Fact]
  public void Execute_ResultForAnotherTick_Throws()
  {
    var calculator = new ScriptedCalculator(input => MovementTickResult.Empty(input.Tick.Next()));

    Assert.Throws<InvalidOperationException>(() => new MovementPhase(calculator).Execute(new TickContext(TestWorld.Create())));
  }

  private static UnitMovementState Find(MovementTickInput input, EntityId unitId) =>
      input.Units.Single(unit => unit.UnitId == unitId);

  private static UnitMovementState MoveTo(MovementTickInput input, EntityId unitId, PositionXZ position)
  {
    UnitMovementState unit = Find(input, unitId);
    return new UnitMovementState(unit.UnitId, unit.Owner, unit.Type, position, unit.Order, unit.RemainingWaypoints, unit.RouteMapRevision);
  }

  private static UnitMovementState AsIdle(MovementTickInput input, EntityId unitId)
  {
    UnitMovementState unit = Find(input, unitId);
    return new UnitMovementState(unit.UnitId, unit.Owner, unit.Type, unit.Position, MovementOrder.Idle, default, unit.RouteMapRevision);
  }

  private static MovementTickResult Result(MovementTickInput input, UnitMovementState update) =>
      new(input.Tick, [update], default, default);

  /// <summary>Calculadora de prueba que responde lo que le indique cada prueba.</summary>
  private sealed class ScriptedCalculator(Func<MovementTickInput, MovementTickResult> respond) : IMovementCalculator
  {
    public MovementTickResult CalculateTick(MovementTickInput input) => respond(input);
  }
}
