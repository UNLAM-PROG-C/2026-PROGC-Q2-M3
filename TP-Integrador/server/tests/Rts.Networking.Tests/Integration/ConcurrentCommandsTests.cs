using System.Text;
using Rts.Shared.Commands;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;

namespace Rts.Networking.Tests.Integration;

/// <summary>
/// AGENTS §7 y contrato 08 §12: «dos jugadores generan comandos concurrentes: secuencias únicas, orden de encolado, sin duplicados».
/// <see cref="NetworkServer"/> real con dos clientes TCP que envían a la vez, y la cola global real de Simulation
/// (<see cref="CommandQueue"/>, contrato 06 §7). GameSession sigue siendo falsa.
/// </summary>
public class ConcurrentCommandsTests
{
  private const int CommandsPerClient = 500;
  private const int Clients = 2;
  private const int TotalCommands = CommandsPerClient * Clients;

  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  private const string HelloLine = "{\"protocolVersion\":1,\"type\":\"Hello\"}";

  private static string StopLine(long messageId) =>
      "{\"protocolVersion\":1,\"type\":\"Stop\",\"messageId\":" + messageId + ",\"payload\":{\"unitIds\":[3]}}";

  private static async Task WaitUntil(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + Patience;
    while (!condition())
    {
      Assert.True(DateTime.UtcNow < deadline, "La condición no se cumplió a tiempo.");
      await Task.Delay(5);
    }
  }

  [Fact]
  public async Task TwoClientsSendingAtOnce_GetUniqueSequencesInQueueOrderWithoutDuplicates()
  {
    var queue = new CommandQueue(TotalCommands);
    await using var harness = new NetworkServerHarness(
        config => config with { CommandsPerSecond = CommandsPerClient, CommandBurst = CommandsPerClient },
        queue);
    await harness.StartAsync();
    TestClient one = await ConnectAsPlayerAsync(harness, PlayerId.One);
    TestClient two = await ConnectAsPlayerAsync(harness, PlayerId.Two);

    // Cada cliente manda sus 500 comandos de una sola vez; los dos receptores admiten a la vez.
    string burst = string.Join("\n", Enumerable.Range(1, CommandsPerClient).Select(id => StopLine(id)));
    await Task.WhenAll(one.SendLineAsync(burst), two.SendLineAsync(burst));
    await WaitUntil(() => queue.PendingCount == TotalCommands);

    IReadOnlyList<CommandEnvelope> admitted = queue.DequeueBatch(TotalCommands);

    // Secuencias únicas y en el orden de la cola: 1, 2, 3… sin huecos.
    Assert.Equal(
        Enumerable.Range(1, TotalCommands).Select(n => (long)n),
        admitted.Select(envelope => envelope.Sequence.Value));

    // Por jugador: los 500, sin duplicados y en el orden en que los mandó su cliente.
    foreach (PlayerId player in new[] { PlayerId.One, PlayerId.Two })
    {
      Assert.Equal(
          Enumerable.Range(1, CommandsPerClient).Select(n => (long)n),
          admitted.Where(envelope => envelope.Player == player).Select(envelope => envelope.ClientCommandId.Value));
    }
  }

  /// <summary>Conecta un cliente, manda su Hello y le asigna el jugador en la GameSession falsa.</summary>
  private static async Task<TestClient> ConnectAsPlayerAsync(NetworkServerHarness harness, PlayerId player)
  {
    int before = harness.Session.Hellos.Count;
    TestClient client = await harness.ConnectAsync();
    await client.SendLineAsync(HelloLine);
    await WaitUntil(() => harness.Session.Hellos.Count == before + 1);
    harness.Gate.PlayersByConnection[harness.Session.Hellos[before]] = player;
    return client;
  }
}
