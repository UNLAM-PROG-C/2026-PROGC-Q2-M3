using Rts.Protocol.Outbound;
using Rts.Protocol.State;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync.Configuration;
using Rts.StateSync.Projection;

namespace Rts.StateSync.Tests.Integration;

/// <summary>
/// Prueba de integración «Eventos por cliente» (work-plan §4, AGENTS §7) y RECOMENDADO 09 §12: se reproducen cientos de ticks
/// con el <see cref="StateSyncPublisher"/> real sobre dos clientes simulados. Los dos terminan con el mismo mundo público, cada uno
/// con sus datos privados correctos, y ninguno recibe los del otro.
/// </summary>
public class ReplayConvergenceTests
{
  /// <summary>Semilla fija: la partida es siempre la misma y un fallo se puede reproducir.</summary>
  private const int Seed = 2026;

  /// <summary>Cantidad de <c>PositionUpdate</c> de la partida: con los valores aprobados son 600 ticks, 30 s de juego.</summary>
  private const int PositionUpdateCount = 300;

  private static readonly StateSyncConfig Config = new();
  private static readonly TickRate Rate = TickRate.Default;

  /// <summary>Termina en un tick con posiciones, para que el último <c>PositionUpdate</c> refleje el mundo final.</summary>
  private static readonly int TickCount = Config.GetPositionIntervalTicks(Rate) * PositionUpdateCount;

  [Fact]
  public void Replay_BothClientsConvergeOnTheSamePublicWorld()
  {
    Replay replay = Run();

    Assert.Equal(replay.Client(PlayerId.One).Public(), replay.Client(PlayerId.Two).Public());
  }

  [Fact]
  public void Replay_EachClientMatchesAnInitialStateOfTheFinalWorld()
  {
    Replay replay = Run();

    foreach (PlayerId player in new[] { PlayerId.One, PlayerId.Two })
    {
      FakeClient replayed = replay.Client(player);
      FakeClient lateJoin = new(replay.FinalInitialState(player));

      Assert.Equal(lateJoin.Public(), replayed.Public());
      Assert.Equal(lateJoin.Private(), replayed.Private());
    }
  }

  [Fact]
  public void Replay_CommandResultsReachOnlyTheirSender()
  {
    Replay replay = Run();

    foreach (PlayerId player in new[] { PlayerId.One, PlayerId.Two })
    {
      Assert.NotEmpty(replay.Match.IssuedCommands(player));
      Assert.Equal(replay.Match.IssuedCommands(player), replay.Client(player).CommandResultIds);
    }
  }

  [Fact]
  public void Replay_BothPlayersReceiveOnlyTheirOwnRepaths()
  {
    Replay replay = Run();

    // El cliente falla si recibe el repath de una unidad ajena; acá solo se verifica que la partida los ejercitó.
    Assert.True(replay.Client(PlayerId.One).RepathCount > 0);
    Assert.True(replay.Client(PlayerId.Two).RepathCount > 0);
  }

  [Fact]
  public void Replay_ExercisesEveryDomainEvent()
  {
    Replay replay = Run();

    Type[] allPayloads = [.. typeof(IDomainEventPayload).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && typeof(IDomainEventPayload).IsAssignableFrom(t))];

    Assert.Equal(allPayloads.Select(t => t.Name).Order(), replay.Match.EmittedPayloadTypes.Select(t => t.Name).Order());
  }

  /// <summary>Juega la partida completa: InitialState para ambos y después <see cref="TickCount"/> ticks publicados.</summary>
  private static Replay Run()
  {
    GameCatalog catalog = CatalogFixture.Create();
    var match = new ScriptedMatch(Seed, catalog);
    var outbox = new ClientsOutbox();
    var publisher = new StateSyncPublisher(new StateProjector(Config, Rate), outbox, catalog);

    publisher.PublishInitialState(SessionId.New(), InitializationId.First, match.World());
    for (int i = 0; i < TickCount; i++)
    {
      publisher.Publish(match.NextTick());
    }

    return new Replay(match, outbox, catalog);
  }

  private sealed record Replay(ScriptedMatch Match, ClientsOutbox Outbox, GameCatalog Catalog)
  {
    public FakeClient Client(PlayerId player) => Outbox.Clients[player];

    /// <summary>Lo que recibiría un cliente que se uniera ahora: sirve de referencia del mundo final.</summary>
    public InitialStateDto FinalInitialState(PlayerId player) =>
        new StateProjector(Config, Rate).BuildInitialState(
            new InitialStateRequest(SessionId.New(), InitializationId.First, player, Match.World(), Catalog));
  }

  /// <summary>Outbox que entrega cada mensaje a su cliente simulado. El InitialState crea el cliente.</summary>
  private sealed class ClientsOutbox : IClientOutbox
  {
    public Dictionary<PlayerId, FakeClient> Clients { get; } = [];

    public void Enqueue(PlayerId recipient, OutboundMessage message)
    {
      if (message.Payload is InitialStateDto initial)
      {
        Clients.Add(recipient, new FakeClient(initial));
        return;
      }
      Clients[recipient].Apply(message);
    }
  }
}
