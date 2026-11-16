using System.Text.Json;
using Rts.Server.Configuration;
using Rts.Shared.Match;

namespace Rts.Server.Tests.EndToEnd;

/// <summary>
/// Prueba de punta a punta (vertical slice, AGENTS §8): el servidor armado como en <c>Program</c>, con TCP real, y dos clientes que
/// hablan JSON por líneas como Godot. Recorre una partida entera: handshake, inicio, comandos de ida y vuelta y apagado ordenado.
/// </summary>
public class FullMatchTests
{
  private const long FirstPlayer = 1;
  private const long SecondPlayer = 2;
  private const long StopCommandId = 1;
  private const long TrainCommandId = 2;

  /// <summary>Plazo para que la partida termine después del Ctrl+C (incluye el drenaje, 2 s en la config de ejemplo).</summary>
  private static readonly TimeSpan MatchEndTimeout = TimeSpan.FromSeconds(10);

  [Fact]
  public async Task TwoClients_PlayAMatch_CommandsGoAndComeBack_AndCtrlCEndsItInOrder()
  {
    int port = ProtocolTestClient.FindFreePort();
    ServerConfig config = ExampleSetup.OnLoopback(ExampleSetup.LoadConfig(), port);
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> match = ExampleSetup.RunMatchAsync(config, shutdown.Token);

    using ProtocolTestClient p1 = await ProtocolTestClient.ConnectAsync(port);
    using ProtocolTestClient p2 = await ProtocolTestClient.ConnectAsync(port);

    // 1. Handshake: cada Hello ocupa una plaza (07 §3).
    await AssertJoinsAsAsync(p1, FirstPlayer);
    await AssertJoinsAsAsync(p2, SecondPlayer);

    // 2. Con las dos plazas ocupadas, cada uno recibe su mundo inicial; con los dos Ready, empieza la partida (07 §5).
    JsonElement world = await p1.ReadUntilAsync("InitialState");
    long initialization = world.GetProperty("initializationId").GetInt64();
    await p2.ReadUntilAsync("InitialState");
    await p1.SendAsync("ClientReady", new { initializationId = initialization });
    await p2.SendAsync("ClientReady", new { initializationId = initialization });
    await p1.ReadUntilAsync("MatchStarted");
    await p2.ReadUntilAsync("MatchStarted");

    // 3. Comandos de P1: red → cola → tick de Simulation → State Sync → red, y el resultado vuelve solo a P1.
    await p1.SendCommandAsync("Stop", StopCommandId, new { unitIds = OwnUnitIds(world, FirstPlayer) });
    await p1.SendCommandAsync("TrainUnit", TrainCommandId, new { producerId = OwnMainCenterId(world, FirstPlayer), unitType = "Worker" });
    Dictionary<long, JsonElement> results = await ReadCommandResultsAsync(p1, StopCommandId, TrainCommandId);

    Assert.Equal("Accepted", results[StopCommandId].GetProperty("outcome").GetString());
    Assert.Equal("Rejected", results[TrainCommandId].GetProperty("outcome").GetString());
    Assert.Equal("Production.NotAvailable", results[TrainCommandId].GetProperty("error").GetString()); // Provisorio (paso 26).

    // 4. Ctrl+C: a cada cliente le llega MatchEnded y DESPUÉS Disconnecting, y el servidor cierra la conexión (D-084, 07 §9).
    await shutdown.CancelAsync();
    await AssertEndedInOrderAsync(p1);
    await AssertEndedInOrderAsync(p2);

    MatchResult result = await match.WaitAsync(MatchEndTimeout);
    Assert.Equal(MatchEndReason.ServerShutdown, result.Reason);
  }

  private static async Task AssertJoinsAsAsync(ProtocolTestClient client, long expectedPlayer)
  {
    await client.SendAsync("Hello", new { clientName = $"test-p{expectedPlayer}" });
    JsonElement assigned = await client.ReadUntilAsync("PlayerAssigned");
    Assert.Equal(expectedPlayer, assigned.GetProperty("playerId").GetInt64());
  }

  private static long[] OwnUnitIds(JsonElement world, long player) =>
  [
    .. world.GetProperty("units").EnumerateArray()
        .Where(unit => unit.GetProperty("owner").GetInt64() == player)
        .Select(unit => unit.GetProperty("unitId").GetInt64()),
  ];

  private static long OwnMainCenterId(JsonElement world, long player) =>
      world.GetProperty("players").EnumerateArray()
          .Single(entry => entry.GetProperty("playerId").GetInt64() == player)
          .GetProperty("mainCenterId").GetInt64();

  /// <summary>Lee lotes <c>StateEvents</c> hasta tener el resultado de cada comando pedido (pueden llegar en ticks distintos).</summary>
  private static async Task<Dictionary<long, JsonElement>> ReadCommandResultsAsync(ProtocolTestClient client, params long[] commandIds)
  {
    var results = new Dictionary<long, JsonElement>();
    while (!commandIds.All(results.ContainsKey))
    {
      JsonElement batch = await client.ReadUntilAsync("StateEvents");
      if (batch.TryGetProperty("commandResults", out JsonElement commandResults))
      {
        foreach (JsonElement commandResult in commandResults.EnumerateArray())
        {
          results[commandResult.GetProperty("clientCommandId").GetInt64()] = commandResult;
        }
      }
    }

    return results;
  }

  /// <summary>
  /// <c>ReadUntilAsync</c> saltea lo que no busca, pero no vuelve atrás: si <c>Disconnecting</c> llegara antes que <c>MatchEnded</c>,
  /// la conexión se cerraría sin encontrar <c>MatchEnded</c> y la prueba fallaría. Así se verifica el orden.
  /// </summary>
  private static async Task AssertEndedInOrderAsync(ProtocolTestClient client)
  {
    JsonElement ended = await client.ReadUntilAsync("MatchEnded");
    Assert.Equal("TechnicalEnd", ended.GetProperty("resultType").GetString());
    Assert.Equal("ServerShutdown", ended.GetProperty("reason").GetString());

    JsonElement disconnecting = await client.ReadUntilAsync("Disconnecting");
    Assert.Equal("Network.ServerShuttingDown", disconnecting.GetProperty("reason").GetString());

    await client.ReadUntilClosedAsync();
  }
}
