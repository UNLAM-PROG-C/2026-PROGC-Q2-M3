using Rts.Server.Composition;
using Rts.Server.Configuration;
using Rts.Server.Validation;
using Rts.Shared.Entities;
using Rts.Shared.Match;
using Rts.Shared.Resources;
using Rts.Shared.World;

namespace Rts.Server.Tests;

public sealed class ServerTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("rts-server-tests-").FullName;

  public void Dispose() => Directory.Delete(_directory, recursive: true);

  /// <summary>Copia el ejemplo a una carpeta temporal con un archivo de mapa vacío al lado, y lo carga.</summary>
  private ServerConfig LoadExample()
  {
    string configPath = Path.Combine(_directory, "server.json");
    File.Copy(Path.Combine(AppContext.BaseDirectory, "server.example.json"), configPath);
    Directory.CreateDirectory(Path.Combine(_directory, "maps"));
    File.WriteAllText(Path.Combine(_directory, "maps", "default.json"), "{}");
    return ServerConfigLoader.Load(configPath);
  }

  [Fact]
  public void Example_LoadsAndPassesAllValidations()
  {
    ServerConfig config = LoadExample();

    Assert.Empty(ConfigurationValidator.Validate(config));
    Assert.Equal(Path.Combine(_directory, "maps", "default.json"), config.MapFile);
    Assert.Equal(20, config.Simulation.TickRate.TicksPerSecond);
    Assert.Equal(TimeSpan.FromMilliseconds(500), config.Networking.CloseLinger);
  }

  [Fact]
  public void Loader_RejectsUnknownAndMissingFields()
  {
    string path = Path.Combine(_directory, "bad.json");

    File.WriteAllText(path, """{ "mapFile": "m.json", "simulaton": {} }""");
    Assert.Throws<ServerConfigException>(() => ServerConfigLoader.Load(path));
    Assert.Throws<ServerConfigException>(() => ServerConfigLoader.Load(Path.Combine(_directory, "no-existe.json")));
  }

  [Fact]
  public void Validator_ReportsCrossModuleMismatches()
  {
    ServerConfig config = LoadExample();
    ServerConfig broken = config with
    {
      Movement = config.Movement with { AttackRange = 2.0 },
      StateSync = config.StateSync with { PositionRateHz = 3 },
      MapFile = Path.Combine(_directory, "falta.json"),
    };

    IReadOnlyList<string> errors = ConfigurationValidator.Validate(broken);

    Assert.Contains(errors, e => e.Contains("D-030"));
    Assert.Contains(errors, e => e.StartsWith("[stateSync]"));
    Assert.Contains(errors, e => e.StartsWith("[mapFile]"));
  }

  [Fact]
  public void Catalog_TakesEachValueFromItsOwnerModule()
  {
    GameCatalog catalog = GameCatalogBuilder.Build(LoadExample());

    UnitCatalogEntry soldier = catalog.Units.Single(u => u.UnitType == UnitType.Soldier);
    BuildingCatalogEntry center = catalog.Buildings.Single(b => b.BuildingType == BuildingType.MainCenter);
    BuildingCatalogEntry tower = catalog.Buildings.Single(b => b.BuildingType == BuildingType.Tower);

    Assert.Equal(ResourceAmount.OfFood(80), soldier.Cost);
    Assert.Equal(300, soldier.TrainingDurationTicks);
    Assert.Equal(1.5, soldier.Attack?.Range);
    Assert.Null(center.Cost);
    Assert.Equal(6.0, tower.Attack?.Range);
    Assert.Null(catalog.Units.Single(u => u.UnitType == UnitType.Worker).Attack);
    Assert.Equal(ResourceAmount.OfWood(30), catalog.FarmRepairCost);
  }

  [Fact]
  public void CommandLine_ParsesOptionsAndOverridesPort()
  {
    Assert.True(CommandLineOptions.TryParse(["--config", "x.json", "--port", "9000", "--validate-only"], out CommandLineOptions options, out _));
    Assert.Equal("x.json", options.ConfigPath);
    Assert.True(options.ValidateOnly);
    Assert.Equal(9000, ServerConfigLoader.ApplyOverrides(LoadExample(), options).Networking.Port);

    Assert.False(CommandLineOptions.TryParse(["--port"], out _, out string? error));
    Assert.Contains("--port", error);
    Assert.True(CommandLineOptions.TryParse([], out CommandLineOptions defaults, out _));
    Assert.Equal(CommandLineOptions.DefaultConfigPath, defaults.ConfigPath);
  }

  [Fact]
  public void ExitCodes_OnlyCriticalErrorFails()
  {
    Assert.Equal(ExitCodes.Success, ExitCodes.FromResult(MatchResult.Draw(new(100))));
    Assert.Equal(ExitCodes.Success, ExitCodes.FromResult(MatchResult.ServerShutdown(null)));
    Assert.Equal(ExitCodes.CriticalError, ExitCodes.FromResult(MatchResult.CriticalError(null)));
  }
}
