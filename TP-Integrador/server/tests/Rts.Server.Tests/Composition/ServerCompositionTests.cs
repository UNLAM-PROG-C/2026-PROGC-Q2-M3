using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rts.Server.Composition;
using Rts.Server.Configuration;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Match;
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
  private const int AnyFreePort = 0;

  /// <summary>Plazo de seguridad: una partida sin jugadores termina en milisegundos; si no termina, la prueba falla en vez de colgarse.</summary>
  private static readonly TimeSpan MatchEndTimeout = TimeSpan.FromSeconds(10);

  private static ServerConfig LoadExampleConfig() => ExampleSetup.LoadConfig();

  private static ServerConfig OnLoopback(ServerConfig config, int port) => ExampleSetup.OnLoopback(config, port);

  private static Task<MatchResult> RunMatchAsync(ServerConfig config, CancellationToken shutdown) =>
      ExampleSetup.RunMatchAsync(config, shutdown);

  private static MapDefinition LoadExampleMap(ServerConfig config) => ExampleSetup.LoadMap(config);

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
  public void RealProduction_AcceptsTrainUnit_StartsTheTrainingAndChargesTheFoodOnce()
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

    // El pedido entra a la cola en la fase 1 y, con el productor libre y comida suficiente, empieza en la fase 8 del mismo tick.
    Assert.Null(outcome.Failure); // Simulation verificó que lo cobrado por Production es el costo de Economy (05 §5).
    CommandResult result = Assert.Single(outcome.Publication!.CommandResults);
    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    ProductionChanged training = outcome.Publication.Events.Select(e => e.Payload).OfType<ProductionChanged>().Last();
    Assert.Equal(ProducerStatus.Training, training.Status);
    Assert.Equal(config.Production.TrainingDurationTicks[UnitType.Worker], training.RemainingTicks);
    ResourcesChanged charged = outcome.Publication.Events.Select(e => e.Payload).OfType<ResourcesChanged>().Single();
    Assert.Equal(config.Economy.InitialBalance.Food - config.Economy.UnitFoodCosts[UnitType.Worker], charged.Balance.Food);
  }

  [Fact]
  public async Task RunMatch_ShutdownBeforeAnyPlayerJoins_EndsInOrderAsServerShutdown()
  {
    ServerConfig config = OnLoopback(LoadExampleConfig(), AnyFreePort);
    using var shutdown = new CancellationTokenSource();

    Task<MatchResult> match = RunMatchAsync(config, shutdown.Token);
    await shutdown.CancelAsync(); // El primer Ctrl+C (D-084).

    // Termina sola: la sesión pide el drenaje y con él la red deja de aceptar. Si algo quedara esperando, vence el plazo.
    MatchResult result = await match.WaitAsync(MatchEndTimeout);
    Assert.Equal(MatchEndReason.ServerShutdown, result.Reason);
    Assert.Null(result.Winner);
  }

  [Fact]
  public async Task RunMatch_PortAlreadyInUse_FailsInsteadOfWaitingForPlayersForever()
  {
    var blocker = new TcpListener(IPAddress.Loopback, AnyFreePort);
    blocker.Start();
    try
    {
      int busyPort = ((IPEndPoint)blocker.LocalEndpoint).Port;
      ServerConfig config = OnLoopback(LoadExampleConfig(), busyPort);

      Task<MatchResult> match = RunMatchAsync(config, CancellationToken.None);

      // Si la composición no reaccionara a la caída de la red, la sesión esperaría jugadores y esto fallaría por el plazo.
      await Assert.ThrowsAsync<SocketException>(() => match.WaitAsync(MatchEndTimeout));
    }
    finally
    {
      blocker.Stop();
    }
  }
}
