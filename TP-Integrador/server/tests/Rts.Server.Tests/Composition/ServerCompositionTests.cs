using Microsoft.Extensions.Logging.Abstractions;
using Rts.Server.Composition;
using Rts.Server.Configuration;
using Rts.Shared.Session;

namespace Rts.Server.Tests.Composition;

/// <summary>
/// La composición del servidor (contrato 10 §4), grupo por grupo. Usa la configuración de ejemplo, igual que al arrancar.
/// </summary>
public class ServerCompositionTests
{
  private static ServerConfig LoadExampleConfig() =>
      ServerConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "server.example.json"));

  [Fact]
  public void CreateTransport_StartsWaitingForPlayers_WithoutConnectionsOrCommands()
  {
    ServerConfig config = LoadExampleConfig();

    TransportPieces transport = ServerComposition.CreateTransport(config, GameCatalogBuilder.Build(config), NullLoggerFactory.Instance);

    Assert.Equal(SessionStatus.WaitingForPlayers, transport.SessionView.Status);
    Assert.False(transport.SessionView.AcceptsGameplay);
    Assert.Empty(transport.Network.GetMetrics());
    Assert.Equal(0, transport.Commands.PendingCount);
  }

  [Fact]
  public void CreateTransport_DoesNotOpenThePort_SoTwoCompositionsWithTheSamePortCoexist()
  {
    ServerConfig config = LoadExampleConfig();
    var catalog = GameCatalogBuilder.Build(config);

    // Si crear el NetworkServer abriera el puerto, el segundo fallaría con "dirección en uso": escuchar es trabajo de RunAsync.
    TransportPieces first = ServerComposition.CreateTransport(config, catalog, NullLoggerFactory.Instance);
    TransportPieces second = ServerComposition.CreateTransport(config, catalog, NullLoggerFactory.Instance);

    Assert.NotSame(first.Network, second.Network);
  }
}
