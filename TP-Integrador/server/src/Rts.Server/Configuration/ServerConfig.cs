using Rts.Combat.Configuration;
using Rts.Construction.Configuration;
using Rts.Economy.Configuration;
using Rts.GameSession.Configuration;
using Rts.Movement.Configuration;
using Rts.Networking.Configuration;
using Rts.Production.Configuration;
using Rts.Simulation.Configuration;
using Rts.StateSync.Configuration;

namespace Rts.Server.Configuration;

/// <summary>
/// Configuración completa del servidor (RECOMENDADO D-083): un solo archivo JSON con una sección por módulo, más la ruta del archivo
/// de mapa (D-046, D-052). Cada sección es la configuración tipada que ya define su módulo; el Server no agrega parámetros de juego.
/// </summary>
public sealed record ServerConfig
{
  /// <summary>Ruta del archivo de mapa. Relativa al archivo de configuración; el cargador la deja absoluta.</summary>
  public required string MapFile { get; init; }

  public required SimulationConfig Simulation { get; init; }

  public required EconomyConfig Economy { get; init; }

  public required CombatConfig Combat { get; init; }

  public required MovementConfig Movement { get; init; }

  public required ConstructionConfig Construction { get; init; }

  public required ProductionConfig Production { get; init; }

  public required GameSessionConfig GameSession { get; init; }

  /// <summary>Opcional: sus valores por defecto son los aprobados (10 Hz).</summary>
  public StateSyncConfig StateSync { get; init; } = new();

  public required NetworkingConfig Networking { get; init; }
}
