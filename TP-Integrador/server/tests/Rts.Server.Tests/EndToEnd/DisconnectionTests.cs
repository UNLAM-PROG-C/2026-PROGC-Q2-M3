using System.Text.Json;
using Rts.Server.Configuration;
using Rts.Shared.Ids;
using Rts.Shared.Match;

namespace Rts.Server.Tests.EndToEnd;

/// <summary>
/// Desconexiones durante la partida contra el servidor real, con TCP (AGENTS §7, work-plan §4: «desconexiones y
/// <c>MatchEnded</c> concurrentes: un solo resultado»). GameSession resuelve el resultado después de detener los ticks (D-055):
/// pase lo que pase a la vez, la partida termina con <b>un</b> resultado oficial y quien sigue conectado recibe <b>un</b>
/// <c>MatchEnded</c>, igual al que devuelve el servidor.
/// </summary>
public class DisconnectionTests
{
  private const long FirstPlayer = 1;
  private const long SecondPlayer = 2;
  private const string MatchEndedType = "MatchEnded";

  /// <summary>Plazo para que la partida termine sola después de las desconexiones (incluye el drenaje de la red).</summary>
  private static readonly TimeSpan MatchEndTimeout = TimeSpan.FromSeconds(10);

  [Fact]
  public async Task WhenOnePlayerDisconnects_TheOtherWins_AndReceivesExactlyOneMatchEnded()
  {
    using var shutdown = new CancellationTokenSource();
    (Task<MatchResult> match, ProtocolTestClient p1, ProtocolTestClient p2) = await StartRunningMatchAsync(shutdown.Token);
    using ProtocolTestClient remaining = p2;

    p1.Dispose(); // P1 pierde la conexión en pleno Running.

    JsonElement ended = await remaining.ReadUntilAsync(MatchEndedType);
    await remaining.ReadUntilClosedAsync();
    MatchResult result = await match.WaitAsync(MatchEndTimeout); // Termina sola, sin Ctrl+C.
    Assert.Equal("Victory", ended.GetProperty("resultType").GetString());
    Assert.Equal("PlayerDisconnected", ended.GetProperty("reason").GetString());
    Assert.Equal(SecondPlayer, ended.GetProperty("winner").GetInt64());
    Assert.Equal(MatchResult.PlayerDisconnected(PlayerId.Two, result.FinalTick), result);
    Assert.Single(remaining.Received, type => type == MatchEndedType);
  }

  [Fact]
  public async Task WhenBothPlayersDisconnectAtOnce_TheMatchEndsWithASingleResult()
  {
    using var shutdown = new CancellationTokenSource();
    (Task<MatchResult> match, ProtocolTestClient p1, ProtocolTestClient p2) = await StartRunningMatchAsync(shutdown.Token);

    await Task.WhenAll(Task.Run(p1.Dispose), Task.Run(p2.Dispose));

    // Si las dos caen en el mismo lote (antes de que termine el tick en curso), nadie gana. Si la segunda llega después de
    // detenidos los ticks, el resultado ya está fijado a favor del que seguía conectado. Las dos salidas son válidas (D-055);
    // lo que no puede pasar es que la partida no termine o que termine con otra cosa.
    MatchResult result = await match.WaitAsync(MatchEndTimeout);
    Assert.Contains(result.Reason, new[] { MatchEndReason.BothPlayersDisconnected, MatchEndReason.PlayerDisconnected });
    Assert.Equal(result.Reason == MatchEndReason.BothPlayersDisconnected, result.Winner is null);
  }

  [Fact]
  public async Task ADisconnectRacingWithCtrlC_GivesOneResult_AndTheRemainingPlayerSeesExactlyThatOne()
  {
    using var shutdown = new CancellationTokenSource();
    (Task<MatchResult> match, ProtocolTestClient p1, ProtocolTestClient p2) = await StartRunningMatchAsync(shutdown.Token);
    using ProtocolTestClient remaining = p2;

    await Task.WhenAll(Task.Run(p1.Dispose), shutdown.CancelAsync()); // La desconexión y el apagado, a la vez.

    JsonElement ended = await remaining.ReadUntilAsync(MatchEndedType);
    await remaining.ReadUntilClosedAsync();
    MatchResult result = await match.WaitAsync(MatchEndTimeout);
    // Gana el orden en que GameSession procesó los dos eventos; cualquiera de los dos resultados es válido (D-055, D-084).
    Assert.Contains(result.Reason, new[] { MatchEndReason.PlayerDisconnected, MatchEndReason.ServerShutdown });
    Assert.Equal(result.Reason.ToString(), ended.GetProperty("reason").GetString());
    Assert.Single(remaining.Received, type => type == MatchEndedType);
  }

  /// <summary>Arranca el servidor, conecta dos clientes y los lleva hasta <c>MatchStarted</c> (07 §5).</summary>
  private static async Task<(Task<MatchResult> Match, ProtocolTestClient P1, ProtocolTestClient P2)> StartRunningMatchAsync(
      CancellationToken shutdown)
  {
    int port = ProtocolTestClient.FindFreePort();
    ServerConfig config = ExampleSetup.OnLoopback(ExampleSetup.LoadConfig(), port);
    Task<MatchResult> match = ExampleSetup.RunMatchAsync(config, shutdown);
    ProtocolTestClient p1 = await JoinAsync(port, FirstPlayer);
    ProtocolTestClient p2 = await JoinAsync(port, SecondPlayer);

    JsonElement world = await p1.ReadUntilAsync("InitialState");
    await p2.ReadUntilAsync("InitialState");
    var ready = new { initializationId = world.GetProperty("initializationId").GetInt64() };
    await p1.SendAsync("ClientReady", ready);
    await p2.SendAsync("ClientReady", ready);
    await p1.ReadUntilAsync("MatchStarted");
    await p2.ReadUntilAsync("MatchStarted");
    return (match, p1, p2);
  }

  private static async Task<ProtocolTestClient> JoinAsync(int port, long expectedPlayer)
  {
    ProtocolTestClient client = await ProtocolTestClient.ConnectAsync(port);
    await client.SendAsync("Hello", new { clientName = $"test-p{expectedPlayer}" });
    JsonElement assigned = await client.ReadUntilAsync("PlayerAssigned");
    Assert.Equal(expectedPlayer, assigned.GetProperty("playerId").GetInt64());
    return client;
  }
}
