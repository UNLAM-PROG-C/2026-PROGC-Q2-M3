using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Simulation.Map;

/// <summary>Error al cargar el mapa: archivo inexistente, JSON inválido, campos faltantes o desconocidos, o mapa inválido.</summary>
public sealed class MapLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Carga el mapa fijo de la partida desde un archivo JSON (DECIDIDO D-046, formato RECOMENDADO D-052; contrato 06 §8).
/// Mismas convenciones que la configuración del servidor (D-083):
/// <list type="bullet">
/// <item>Propiedades en camelCase, comentarios <c>//</c> y comas finales permitidos.</item>
/// <item>Un campo desconocido, uno obligatorio que falta o un <c>null</c> son error.</item>
/// <item>Los tipos de unidad van como string; un número se rechaza (D-072).</item>
/// </list>
/// </summary>
public static class MapLoader
{
  private static readonly JsonSerializerOptions Options = CreateOptions();

  /// <summary>Lee, interpreta y valida el mapa contra la huella del Centro Principal.</summary>
  /// <exception cref="MapLoadException">Si el archivo no existe, no se puede interpretar o el mapa es inválido.</exception>
  public static MapDefinition Load(string path, FootprintSize mainCenterSize)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    string fullPath = Path.GetFullPath(path);
    if (!File.Exists(fullPath))
    {
      throw new MapLoadException($"No existe el archivo de mapa '{fullPath}'.");
    }

    MapDefinition map = Parse(File.ReadAllText(fullPath));
    EnsureValid(map, mainCenterSize, fullPath);
    return map;
  }

  /// <summary>Interpreta el JSON del mapa sin validar sus reglas de contenido (eso lo hace <see cref="Load"/>).</summary>
  /// <exception cref="MapLoadException">Si el JSON es inválido o tiene valores imposibles (por ejemplo, tamaño de celda negativo).</exception>
  public static MapDefinition Parse(string json)
  {
    ArgumentNullException.ThrowIfNull(json);
    try
    {
      MapFile file = JsonSerializer.Deserialize<MapFile>(json, Options)
          ?? throw new MapLoadException("El archivo de mapa está vacío.");
      return file.ToDefinition();
    }
    catch (Exception ex) when (ex is JsonException or ArgumentException)
    {
      throw new MapLoadException($"Mapa inválido: {ex.Message}", ex);
    }
  }

  private static void EnsureValid(MapDefinition map, FootprintSize mainCenterSize, string fullPath)
  {
    IReadOnlyList<string> errors = map.GetValidationErrors(mainCenterSize);
    if (errors.Count > 0)
    {
      throw new MapLoadException($"Mapa inválido en '{fullPath}':{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }
  }

  private static JsonSerializerOptions CreateOptions()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
      UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
      RespectNullableAnnotations = true,
      RespectRequiredConstructorParameters = true,
    };
    options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    options.MakeReadOnly(populateMissingResolver: true);
    return options;
  }

  // Esquema del archivo (D-052). Son tipos de lectura privados: el resto del servidor solo ve MapDefinition.

  private sealed record MapFile(double CellSize, int Columns, int Rows, IReadOnlyList<CellEntry> Trees, IReadOnlyList<StartEntry> Starts)
  {
    public MapDefinition ToDefinition() => new(
        new GridSpec(CellSize, Columns, Rows),
        [.. Trees.Select(tree => tree.ToCell())],
        [.. Starts.Select(start => start.ToDefinition())]);
  }

  private sealed record CellEntry(int Column, int Row)
  {
    public GridCell ToCell() => new(Column, Row);
  }

  private sealed record StartEntry(int Player, CellEntry MainCenterAnchor, IReadOnlyList<UnitEntry> InitialUnits)
  {
    public PlayerStartDefinition ToDefinition() => new(
        new PlayerId(Player),
        MainCenterAnchor.ToCell(),
        [.. InitialUnits.Select(unit => unit.ToDefinition())]);
  }

  private sealed record UnitEntry(UnitType Type, double X, double Z)
  {
    public InitialUnitDefinition ToDefinition() => new(Type, new PositionXZ(X, Z));
  }
}
