namespace Rts.Server.Configuration;

/// <summary>
/// Argumentos de línea de comandos (RECOMENDADO D-083):
/// <code>rts-server [--config &lt;ruta&gt;] [--port &lt;puerto&gt;] [--validate-only]</code>
/// </summary>
public sealed record CommandLineOptions
{
  public const string DefaultConfigPath = "server.json";

  public const string Usage = "Uso: rts-server [--config <ruta>] [--port <puerto>] [--validate-only]";

  /// <summary>Archivo de configuración; por defecto <c>server.json</c> en el directorio actual.</summary>
  public string ConfigPath { get; init; } = DefaultConfigPath;

  /// <summary>Reemplaza <c>networking.port</c> del archivo, para lanzar el servidor en otro puerto sin editarlo.</summary>
  public int? Port { get; init; }

  /// <summary>Solo carga y valida la configuración, muestra el resumen y sale.</summary>
  public bool ValidateOnly { get; init; }

  /// <summary>Interpreta los argumentos. Devuelve <c>false</c> con un mensaje si hay un argumento desconocido o incompleto.</summary>
  public static bool TryParse(IReadOnlyList<string> args, out CommandLineOptions options, out string? error)
  {
    ArgumentNullException.ThrowIfNull(args);
    options = new CommandLineOptions();
    error = null;

    for (int i = 0; i < args.Count; i++)
    {
      switch (args[i])
      {
        case "--config" when i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]):
          options = options with { ConfigPath = args[++i] };
          break;

        case "--port" when i + 1 < args.Count && int.TryParse(args[i + 1], out int port):
          options = options with { Port = port };
          i++;
          break;

        case "--validate-only":
          options = options with { ValidateOnly = true };
          break;

        default:
          error = $"Argumento desconocido o incompleto: '{args[i]}'. {Usage}";
          return false;
      }
    }

    return true;
  }
}
