using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Rts.Server;
using Rts.Server.Composition;
using Rts.Server.Configuration;
using Rts.Server.Validation;
using Rts.Shared.Match;
using Rts.Shared.World;

// Programa principal (contrato 10). Se ejecuta una vez: leer, validar, componer, esperar el fin y salir. No contiene lógica de
// juego; el bucle de ticks vive en Simulation.

// Logging de consola asincrónico: escribe en segundo plano y no frena al hilo de Simulation (D-083).
using ILoggerFactory loggers = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(options =>
    {
      options.SingleLine = true;
      options.TimestampFormat = "HH:mm:ss.fff ";
    })
    .SetMinimumLevel(LogLevel.Information));
ILogger log = loggers.CreateLogger("Rts.Server");

// 1. Argumentos.
if (!CommandLineOptions.TryParse(args, out CommandLineOptions options, out string? argumentError))
{
  log.LogError("{Error}", argumentError);
  return ExitCodes.InvalidArguments;
}

// 2. Configuración: leer y validar todo antes de crear nada.
ServerConfig config;
try
{
  config = ServerConfigLoader.ApplyOverrides(ServerConfigLoader.Load(options.ConfigPath), options);
}
catch (ServerConfigException ex)
{
  log.LogError("{Error}", ex.Message);
  return ExitCodes.InvalidConfiguration;
}

IReadOnlyList<string> errors = ConfigurationValidator.Validate(config);
if (errors.Count > 0)
{
  foreach (string error in errors)
  {
    log.LogError("{Error}", error);
  }

  log.LogError("Configuración inválida: {Count} problema(s). El servidor no arranca.", errors.Count);
  return ExitCodes.InvalidConfiguration;
}

GameCatalog catalog = GameCatalogBuilder.Build(config);
log.LogInformation(
    "Configuración válida: {Address}:{Port}, {TickRate} ticks/s, posiciones a {PositionRate} Hz, mapa '{Map}'.",
    config.Networking.ListenAddress, config.Networking.Port, config.Simulation.TickRate.TicksPerSecond, config.StateSync.PositionRateHz, config.MapFile);

if (options.ValidateOnly)
{
  return ExitCodes.Success;
}

// 3. Apagado ordenado (D-084): el primer Ctrl+C (o SIGTERM) pide terminar la partida con orden; el segundo corta en seco.
using var shutdown = new CancellationTokenSource();
void OnSignal(PosixSignalContext context)
{
  if (shutdown.IsCancellationRequested)
  {
    return; // Segundo aviso: se deja la acción por defecto (terminar el proceso).
  }

  context.Cancel = true;
  log.LogWarning("Apagado pedido ({Signal}): cerrando la partida. Otro Ctrl+C corta en seco.", context.Signal);
  shutdown.Cancel();
}

using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);

// 4. Partida.
try
{
  MatchResult result = await ServerComposition.RunMatchAsync(config, catalog, loggers, shutdown.Token);
  log.LogInformation("Partida terminada: {Type}, ganador {Winner}, motivo {Reason}.", result.ResultType, result.Winner?.ToString() ?? "ninguno", result.Reason);
  return ExitCodes.FromResult(result);
}
catch (NotImplementedException ex)
{
  // TEMPORAL hasta la vertical slice (contrato 10 §6).
  log.LogError("{Error}", ex.Message);
  return ExitCodes.NotYetComposed;
}
