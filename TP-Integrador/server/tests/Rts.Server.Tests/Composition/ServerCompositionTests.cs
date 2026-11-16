using Microsoft.Extensions.Logging.Abstractions;
using Rts.Server.Composition;
using Rts.Server.Configuration;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Ids;
using Rts.Shared.Session;
using Rts.Shared.World;
using Rts.Simulation;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Map;

namespace Rts.Server.Tests.Composition;

/// <summary>
/// La composición del servidor (contrato 10 §4), grupo por grupo. Usa la configuración y el mapa de ejemplo, igual que al arrancar.
/// </summary>
public class ServerCompositionTests
{
  private const int MainCentersInAMatch = 2;

  private static ServerConfig LoadExampleConfig() =>
      ServerConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "server.example.json"));

  private static MapDefinition LoadExampleMap(ServerConfig config) =>
      MapLoader.Load(config.MapFile, config.Construction.FootprintSizes[BuildingType.MainCenter]);

  private static TransportPieces CreateTransport(ServerConfig config) =>
      ServerComposition.CreateTransport(config, GameCatalogBuilder.Build(config), NullLoggerFactory.Instance);

  [Fact]
  public void CreateTransport_StartsWaitingForPlayers_WithoutConnectionsOrCommands()
  {
    ServerConfig config = LoadExampleConfig();

    TransportPieces transport = CreateTransport(config);

    Assert.Equal(SessionStatus.WaitingForPlayers, transport.SessionView.Status);
    Assert.False(transport.SessionView.AcceptsGameplay);
    Assert.Empty(transport.Network.GetMetrics());
    Assert.Equal(0, transport.Commands.PendingCount);
  }

  [Fact]
  public void CreateTransport_DoesNotOpenThePort_SoTwoCompositionsWithTheSamePortCoexist()
  {
    ServerConfig config = LoadExampleConfig();

    // Si crear el NetworkServer abriera el puerto, el segundo fallaría con "dirección en uso": escuchar es trabajo de RunAsync.
    TransportPieces first = CreateTransport(config);
    TransportPieces second = CreateTransport(config);

    Assert.NotSame(first.Network, second.Network);
  }

  [Fact]
  public void CreateSimulation_BuildsTheMatchFromTheMap_WithoutStartingTicks()
  {
    ServerConfig config = LoadExampleConfig();
    TransportPieces transport = CreateTransport(config);

    SimulationPieces simulation = ServerComposition.CreateSimulation(
        config, LoadExampleMap(config), transport, NullLoggerFactory.Instance);
    WorldView initial = simulation.Loop.InitializeMatch(); // Lo que hará GameSession al tener los dos Ready.

    Assert.False(simulation.Loop.IsRunning); // Los ticks los arranca GameSession con StartTicks, no la composición.
    Assert.Equal(MainCentersInAMatch, initial.Buildings.Count(building => building.BuildingType == BuildingType.MainCenter));
  }

  [Fact]
  public void ProvisionalProduction_RejectsTrainUnit_WithoutEndingTheMatch()
  {
    ServerConfig config = LoadExampleConfig();
    var commands = new CommandQueue(config.Simulation.CommandQueueCapacity);
    ISimulationEngine engine = SimulationFactory.CreateEngine(
        config.Simulation,
        LoadExampleMap(config),
        EconomyParameters.FromConfig(config.Economy),
        commands,
        ServerComposition.CreateDomainModules(config),
        TimeProvider.System);
    WorldView initial = engine.InitializeMatch();
    BuildingView mainCenter = initial.Buildings.First(building =>
        building.Owner == PlayerId.One && building.BuildingType == BuildingType.MainCenter);

    commands.Admit(PlayerId.One, new ClientCommandId(1), new TrainUnitCommand(mainCenter.BuildingId, UnitType.Worker));
    TickOutcome outcome = engine.AdvanceTick();

    Assert.Null(outcome.Failure); // Rechazar no es una falla: la partida sigue.
    CommandResult result = Assert.Single(outcome.Publication!.CommandResults);
    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Equal(UnavailableProduction.NotAvailable, result.Error);
  }

  [Fact]
  public void ProvisionalProduction_AnswersTrainingDurationFromTheConfig()
  {
    ServerConfig config = LoadExampleConfig();
    var production = new UnavailableProduction(config.Production);

    Assert.Equal(
        config.Production.TrainingDurationTicks[UnitType.Soldier],
        production.GetTrainingDurationTicks(UnitType.Soldier));
  }
}
