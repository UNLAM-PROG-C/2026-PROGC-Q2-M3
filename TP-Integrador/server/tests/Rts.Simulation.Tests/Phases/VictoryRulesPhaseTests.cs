using Rts.Combat.Contracts;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Configuration;
using Rts.Simulation.Engine;
using Rts.Simulation.Phases;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Phases;

public class VictoryRulesPhaseTests
{
  private static TickContext ContextWithout(params EntityId[] fallenCenters)
  {
    WorldState world = TestWorld.Create();
    world = world.With(buildings: world.Buildings.RemoveRange(fallenCenters));
    return new TickContext(world);
  }

  [Fact]
  public void BothBasesStanding_NoResult()
  {
    TickContext context = ContextWithout();

    new VictoryRulesPhase().Execute(context);

    Assert.Null(context.World.GameplayResult);
  }

  [Theory]
  [InlineData(1, 2)] // Cayó la base de P1 ⇒ gana P2.
  [InlineData(2, 1)] // Cayó la base de P2 ⇒ gana P1.
  public void OneBaseFell_TheOtherPlayerWins(int loser, int winner)
  {
    EntityId fallen = loser == PlayerId.One.Value ? TestWorld.P1Center : TestWorld.P2Center;
    TickContext context = ContextWithout(fallen);

    new VictoryRulesPhase().Execute(context);

    Assert.Equal(MatchResult.BaseDestroyed(new PlayerId(winner), context.Tick), context.World.GameplayResult);
  }

  [Fact]
  public void BothBasesFellInTheSameTick_IsADraw()
  {
    TickContext context = ContextWithout(TestWorld.P1Center, TestWorld.P2Center);

    new VictoryRulesPhase().Execute(context);

    Assert.Equal(MatchResult.Draw(context.Tick), context.World.GameplayResult);
  }

  [Fact]
  public void ThroughTheFactory_CombatDestroysABase_TheTickPublishesTheResultAndTheEngineStops()
  {
    SimulationConfig config = ConfigAndMapTests.ValidConfig();
    var combat = new FakeCombatCalculator
    {
      Respond = input => CombatTickResult.Empty(input.Tick) with
      {
        Damage = [new TargetDamage(TestWorld.P2Center, FakeConstructionRules.MainCenterMaxHp, FakeConstructionRules.MainCenterMaxHp)],
      },
    };
    ISimulationEngine engine = SimulationFactory.CreateEngine(
        config, TestWorld.Map, TestWorld.Economy, new CommandQueue(config.CommandQueueCapacity),
        UnusedModules.Create(combatCalculator: combat), TimeProvider.System);
    engine.InitializeMatch();

    TickOutcome outcome = engine.AdvanceTick();

    MatchResult expected = MatchResult.BaseDestroyed(PlayerId.One, outcome.Publication!.Tick);
    Assert.Equal(expected, outcome.GameplayResult);
    Assert.Equal(expected, outcome.Publication.GameplayResult);
    Assert.Throws<InvalidOperationException>(() => engine.AdvanceTick()); // Ningún tick después del resultado.
  }
}
