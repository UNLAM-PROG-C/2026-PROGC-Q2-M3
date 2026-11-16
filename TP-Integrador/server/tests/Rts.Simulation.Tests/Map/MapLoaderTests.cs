using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Map;

namespace Rts.Simulation.Tests.Map;

public class MapLoaderTests
{
  private static readonly FootprintSize CenterSize = new(4, 4);

  // Ejemplo del contrato 06 §8 (formato D-052), con un comentario y una coma final para probar la lectura permisiva.
  private const string ValidJson = """
      {
        // Mapa de prueba
        "cellSize": 1.0,
        "columns": 64,
        "rows": 64,
        "trees": [ { "column": 10, "row": 20 }, { "column": 53, "row": 43 } ],
        "starts": [
          { "player": 1, "mainCenterAnchor": { "column": 4, "row": 4 },
            "initialUnits": [ { "type": "Worker", "x": 9.5, "z": 5.5 } ] },
          { "player": 2, "mainCenterAnchor": { "column": 56, "row": 56 },
            "initialUnits": [ { "type": "Worker", "x": 54.5, "z": 58.5 } ] },
        ],
      }
      """;

  private static readonly MapDefinition ExpectedMap = new(
      new GridSpec(1.0, 64, 64),
      [new GridCell(10, 20), new GridCell(53, 43)],
      [
        new PlayerStartDefinition(PlayerId.One, new GridCell(4, 4),
            [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(9.5, 5.5))]),
        new PlayerStartDefinition(PlayerId.Two, new GridCell(56, 56),
            [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(54.5, 58.5))]),
      ]);

  [Fact]
  public void Parse_ContractExample_BuildsExpectedDefinition()
  {
    Assert.Equal(ExpectedMap, MapLoader.Parse(ValidJson));
  }

  [Theory]
  [InlineData("\"rows\": 64,", "\"rows\": 64, \"extra\": 1,")]               // campo desconocido
  [InlineData("\"rows\": 64,", "")]                                         // campo obligatorio faltante
  [InlineData("\"type\": \"Worker\", \"x\": 9.5", "\"type\": 1, \"x\": 9.5")] // enum como número
  [InlineData("\"type\": \"Worker\", \"x\": 9.5", "\"type\": \"Dragon\", \"x\": 9.5")] // tipo inexistente
  [InlineData("[ { \"column\": 10, \"row\": 20 }, { \"column\": 53, \"row\": 43 } ]", "null")] // colección nula
  [InlineData("\"cellSize\": 1.0", "\"cellSize\": -1.0")]                   // valor imposible
  [InlineData("{", "[")]                                                   // JSON mal formado
  public void Parse_InvalidJson_ThrowsMapLoadException(string original, string replacement)
  {
    string json = ReplaceFirst(ValidJson, original, replacement);

    Assert.Throws<MapLoadException>(() => MapLoader.Parse(json));
  }

  [Fact]
  public void Load_ValidFile_ReturnsDefinition()
  {
    string path = WriteTempFile(ValidJson);
    try
    {
      Assert.Equal(ExpectedMap, MapLoader.Load(path, CenterSize));
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Fact]
  public void Load_MissingFile_ThrowsMapLoadException()
  {
    string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    Assert.Throws<MapLoadException>(() => MapLoader.Load(path, CenterSize));
  }

  [Fact]
  public void Load_ContentRuleViolation_ThrowsWithValidationErrors()
  {
    // P2 con el ancla de P1: los Centros Principales se superponen (MapDefinition.GetValidationErrors).
    string json = ReplaceFirst(ValidJson, "{ \"column\": 56, \"row\": 56 }", "{ \"column\": 4, \"row\": 4 }");
    string path = WriteTempFile(json);
    try
    {
      MapLoadException error = Assert.Throws<MapLoadException>(() => MapLoader.Load(path, CenterSize));
      Assert.Contains("superponen", error.Message);
    }
    finally
    {
      File.Delete(path);
    }
  }

  private static string ReplaceFirst(string text, string original, string replacement)
  {
    int index = text.IndexOf(original, StringComparison.Ordinal);
    Assert.True(index >= 0, $"El texto de prueba no contiene '{original}'.");
    return string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + original.Length));
  }

  private static string WriteTempFile(string content)
  {
    string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    File.WriteAllText(path, content);
    return path;
  }
}
