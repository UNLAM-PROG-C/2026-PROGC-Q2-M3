using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Protocol.State;

namespace Rts.Protocol.Tests.Examples;

/// <summary>
/// Mantiene <c>docs/protocol/examples/</c> idéntico a lo que producen los DTOs reales. Si un DTO cambia, esta prueba falla hasta
/// regenerar los ejemplos:
/// <code>RTS_UPDATE_PROTOCOL_EXAMPLES=1 dotnet test server/tests/Rts.Protocol.Tests</code>
/// (en PowerShell: <c>$env:RTS_UPDATE_PROTOCOL_EXAMPLES=1; dotnet test server/tests/Rts.Protocol.Tests</c>).
/// </summary>
public class ProtocolExamplesTests
{
  private const string UpdateVariable = "RTS_UPDATE_PROTOCOL_EXAMPLES";

  private static readonly string[] ServerToClientTypes =
  [
      ProtocolMessageTypes.PlayerAssigned, ProtocolMessageTypes.StartAborted, ProtocolMessageTypes.MatchStarted,
        ProtocolMessageTypes.MatchEnded, ProtocolMessageTypes.SessionRequestRejected, ProtocolMessageTypes.InitialState,
        ProtocolMessageTypes.StateEvents, ProtocolMessageTypes.PositionUpdate, ProtocolMessageTypes.CommandRejected,
        ProtocolMessageTypes.ProtocolError, ProtocolMessageTypes.Disconnecting, ProtocolMessageTypes.Ping, ProtocolMessageTypes.Pong,
    ];

  [Fact]
  public void ExampleFiles_MatchTheRealDtos()
  {
    string directory = ExamplesDirectory();
    bool update = Environment.GetEnvironmentVariable(UpdateVariable) == "1";
    var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var outdated = new List<string>();

    foreach (ProtocolExamples.Example example in ProtocolExamples.All)
    {
      string path = Path.GetFullPath(Path.Combine(directory, example.RelativePath));
      string expected = ProtocolExamples.Render(example);
      expectedPaths.Add(path);

      if (update)
      {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, expected);
      }
      else if (!File.Exists(path) || Normalize(File.ReadAllText(path)) != Normalize(expected))
      {
        outdated.Add(example.RelativePath);
      }
    }

    string[] stale = Directory.Exists(directory)
        ? Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Where(p => !expectedPaths.Contains(Path.GetFullPath(p))).ToArray()
        : [];
    if (update)
    {
      Array.ForEach(stale, File.Delete);
      return;
    }

    outdated.AddRange(stale.Select(p => Path.GetRelativePath(directory, p) + " (sobra)"));
    Assert.True(
        outdated.Count == 0,
        $"Ejemplos del protocolo desactualizados: {string.Join(", ", outdated)}. Regenerar con {UpdateVariable}=1 dotnet test server/tests/Rts.Protocol.Tests");
  }

  [Fact]
  public void Examples_CoverEveryMessageType()
  {
    string[] clientTypes = ProtocolExamples.All.Where(e => e.RelativePath.StartsWith("client-to-server/")).Select(e => e.Type).ToArray();
    string[] serverTypes = ProtocolExamples.All.Where(e => e.RelativePath.StartsWith("server-to-client/")).Select(e => e.Type).ToArray();

    Assert.All(ProtocolMessageTypes.ClientToServer, type => Assert.Contains(type, clientTypes));
    Assert.All(ServerToClientTypes, type => Assert.Contains(type, serverTypes));
  }

  [Fact]
  public void Examples_CoverEveryStateEvent()
  {
    Type[] declared = typeof(StateEventDto).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(a => a.DerivedType).ToArray();
    Type[] used = ProtocolExamples.All.Select(e => e.Payload).OfType<StateEventsDto>().SelectMany(b => b.Events).Select(e => e.GetType()).Distinct().ToArray();

    Assert.All(declared, type => Assert.Contains(type, used));
  }

  [Fact]
  public void Examples_RoundTripThroughTheirDto()
  {
    foreach (ProtocolExamples.Example example in ProtocolExamples.All.Where(e => e.Payload is not null))
    {
      NetworkEnvelope envelope = JsonSerializer.Deserialize<NetworkEnvelope>(ProtocolExamples.Render(example), ProtocolJson.Options)!;
      object payload = envelope.Payload!.Value.Deserialize(example.Payload!.GetType(), ProtocolJson.Options)!;

      Assert.Equal(ProtocolExamples.Render(example), ProtocolExamples.Render(example with { Payload = payload }));
    }
  }

  private static string Normalize(string text) => text.Replace("\r\n", "\n");

  /// <summary>Busca la raíz del repositorio (la carpeta que contiene AGENTS.md) desde la carpeta de salida de las pruebas.</summary>
  private static string ExamplesDirectory()
  {
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
      if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
      {
        return Path.Combine(dir.FullName, "docs", "protocol", "examples");
      }
    }

    throw new InvalidOperationException("No se encontró la raíz del repositorio (AGENTS.md).");
  }
}
