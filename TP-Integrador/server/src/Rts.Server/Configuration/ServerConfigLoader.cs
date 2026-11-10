using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rts.Server.Configuration;

/// <summary>Error al leer el archivo de configuración: no existe, JSON inválido, faltan campos o hay campos desconocidos.</summary>
public sealed class ServerConfigException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Lee <see cref="ServerConfig"/> desde JSON con el lector de .NET, sin paquetes extra (D-083). No valida reglas: eso lo hace
/// <see cref="Validation.ConfigurationValidator"/>.
/// <list type="bullet">
/// <item>Propiedades en camelCase (sin distinguir mayúsculas), enums como string, comentarios <c>//</c> y comas finales permitidos.</item>
/// <item>Un campo desconocido es error: así un nombre mal escrito no pasa desapercibido con el valor por defecto.</item>
/// <item>Los campos <c>required</c> que falten son error.</item>
/// <item>Los <c>TimeSpan</c> se escriben como <c>"hh:mm:ss.fff"</c>.</item>
/// </list>
/// </summary>
public static class ServerConfigLoader
{
  private static readonly JsonSerializerOptions Options = CreateOptions();

  /// <summary>Lee el archivo y deja <see cref="ServerConfig.MapFile"/> como ruta absoluta, relativa a la carpeta del archivo.</summary>
  /// <exception cref="ServerConfigException">Si el archivo no existe o no se puede interpretar.</exception>
  public static ServerConfig Load(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    string fullPath = Path.GetFullPath(path);
    if (!File.Exists(fullPath))
    {
      throw new ServerConfigException($"No existe el archivo de configuración '{fullPath}'.");
    }

    ServerConfig? config;
    try
    {
      config = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(fullPath), Options);
    }
    catch (JsonException ex)
    {
      throw new ServerConfigException($"Configuración inválida en '{fullPath}': {ex.Message}", ex);
    }

    if (config is null)
    {
      throw new ServerConfigException($"El archivo '{fullPath}' está vacío.");
    }

    string baseDirectory = Path.GetDirectoryName(fullPath)!;
    return config with { MapFile = Path.GetFullPath(Path.Combine(baseDirectory, config.MapFile)) };
  }

  /// <summary>Aplica los argumentos que reemplazan valores del archivo (hoy, solo el puerto).</summary>
  public static ServerConfig ApplyOverrides(ServerConfig config, CommandLineOptions options)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(options);
    return options.Port is { } port ? config with { Networking = config.Networking with { Port = port } } : config;
  }

  private static JsonSerializerOptions CreateOptions()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
      UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    options.Converters.Add(new JsonStringEnumConverter());
    options.Converters.Add(new FootprintSizeJsonConverter());
    options.MakeReadOnly(populateMissingResolver: true);
    return options;
  }
}
