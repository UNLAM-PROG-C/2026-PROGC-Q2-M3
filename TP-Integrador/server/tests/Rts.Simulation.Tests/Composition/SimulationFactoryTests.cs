using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.World;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Configuration;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Composition;

public class SimulationFactoryTests
{
  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
  private static readonly PositionXZ Waypoint = new(12.5, 12.5);

  private static ISimulationEngine CreateEngine(CommandQueue queue, SimulationConfig? config = null) =>
      SimulationFactory.CreateEngine(
          config ?? ConfigAndMapTests.ValidConfig(), TestWorld.Map, TestWorld.Economy, queue, TestModules.Create(),
          TimeProvider.System);

  [Fact]
  public void CreateEngine_WiresTheImplementedHandlersAndPhases()
  {
    SimulationConfig config = ConfigAndMapTests.ValidConfig();
    var queue = new CommandQueue(config.CommandQueueCapacity);
    ISimulationEngine engine = CreateEngine(queue, config);
    engine.InitializeMatch();
    var route = new UnitRouteCandidate(TestWorld.P1WorkerA, [Waypoint, TestWorld.SomeDestination]);
    var workerBRoute = new UnitRouteCandidate(TestWorld.P1WorkerB, [Waypoint]);

    queue.Admit(PlayerId.One, new ClientCommandId(1), new MoveCommand(TestWorld.SomeDestination, MapRevision.Initial, [route]));
    queue.Admit(PlayerId.One, new ClientCommandId(2), new RepairFarmCommand(TestWorld.Missing, MapRevision.Initial, [route]));
    queue.Admit(PlayerId.One, new ClientCommandId(3), new TrainUnitCommand(TestWorld.P1Center, UnitType.Worker));
    queue.Admit(PlayerId.One, new ClientCommandId(4), new AttackCommand(TestWorld.P2Worker, MapRevision.Initial, [route]));
    queue.Admit(PlayerId.One, new ClientCommandId(5), new GatherWoodCommand(TestWorld.Tree, MapRevision.Initial, [workerBRoute]));
    queue.Admit(PlayerId.One, new ClientCommandId(6), new RepathResponseCommand(TestWorld.P2Worker, new OrderId(1), MapRevision.Initial, []));
    TickOutcome first = engine.AdvanceTick();
    engine.AdvanceTick();

    CommandResult[] results = [.. first.Publication!.CommandResults];
    Assert.Equal(CommandOutcome.Accepted, results[0].Outcome); // Move: handler conectado.
    Assert.Equal(SimulationErrors.UnsupportedCommand, results[1].Error); // RepairFarm: todavía sin handler.
    Assert.Equal(CommandOutcome.Accepted, results[2].Outcome); // TrainUnit: handler conectado.
    Assert.Equal(CommonErrors.IncompatibleEntity, results[3].EntityResults[0].Error); // Attack: conectado (un trabajador no ataca).
    Assert.Equal(CommandOutcome.Accepted, results[4].Outcome); // GatherWood: handler conectado.
    Assert.Equal(CommonErrors.NotOwner, results[5].Error); // RepathResponse: conectado (la unidad es de P2).
    Assert.Equal(TestWorld.SomeDestination, engine.Current!.Units[TestWorld.P1WorkerA].Position); // Fase 2 conectada.
  }

  [Fact]
  public void CreateEngine_InvalidConfig_Throws()
  {
    SimulationConfig invalid = ConfigAndMapTests.ValidConfig() with { MaxCommandsPerTick = 0 };

    Assert.Throws<ArgumentException>(() => CreateEngine(new CommandQueue(1), invalid));
  }

  [Fact]
  public async Task CreateLoop_StartsAndStopsAroundTheEngine()
  {
    SimulationConfig config = ConfigAndMapTests.ValidConfig();
    ISimulationEngine engine = CreateEngine(new CommandQueue(config.CommandQueueCapacity), config);
    var sink = new IgnoringSink();
    ISimulationLoop loop = SimulationFactory.CreateLoop(engine, config, sink, sink, TimeProvider.System);

    loop.InitializeMatch();
    loop.StartTicks();
    TickId final = await loop.StopTicksAsync().WaitAsync(TestTimeout);

    Assert.False(loop.IsRunning);
    Assert.Equal(engine.Current!.Tick, final);
  }

  private sealed class IgnoringSink : ITickPublisher, IMatchResultSink
  {
    public void Publish(TickPublication publication)
    {
    }

    public void OnSimulationResult(MatchResult result)
    {
    }
  }
}
