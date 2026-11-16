using Microsoft.Extensions.Logging.Abstractions;
using Rts.Server.Composition;
using Rts.Server.Configuration;
using Rts.Shared.Entities;
using Rts.Shared.Match;
using Rts.Simulation.Map;

namespace Rts.Server.Tests;

/// <summary>La configuración y el mapa de ejemplo, como al arrancar el servidor, y una partida completa sobre ellos.</summary>
internal static class ExampleSetup
{
  private const string Loopback = "127.0.0.1";

  public static ServerConfig LoadConfig() =>
      ServerConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "server.example.json"));

  public static MapDefinition LoadMap(ServerConfig config) =>
      MapLoader.Load(config.MapFile, config.Construction.FootprintSizes[BuildingType.MainCenter]);

  /// <summary>Solo en la máquina local (sin pedir permisos de firewall) y en el puerto indicado.</summary>
  public static ServerConfig OnLoopback(ServerConfig config, int port) =>
      config with { Networking = config.Networking with { ListenAddress = Loopback, Port = port } };

  /// <summary>Lo mismo que hace <c>Program</c> después de validar: componer y correr una partida.</summary>
  public static Task<MatchResult> RunMatchAsync(ServerConfig config, CancellationToken shutdown) =>
      ServerComposition.RunMatchAsync(
          config, LoadMap(config), GameCatalogBuilder.Build(config), NullLoggerFactory.Instance, shutdown);
}
