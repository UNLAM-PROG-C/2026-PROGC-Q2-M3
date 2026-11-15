using Rts.Networking.Admission;
using Rts.Shared.Commands;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking.Tests.Admission;

// Piezas falsas para probar la admisión sin GameSession ni cola de comandos reales. Cada una anota cómo la llamaron.

internal sealed class FakeSessionGate : ISessionGate
{
  public SessionStatus Status { get; set; } = SessionStatus.Running;

  public bool AcceptsGameplay { get; set; } = true;

  /// <summary>Jugador que devuelve <see cref="GetPlayer"/>; <c>null</c> simula una conexión sin plaza.</summary>
  public PlayerId? Player { get; set; } = PlayerId.One;

  public List<ConnectionId> PlayerLookups { get; } = [];

  public PlayerId? GetPlayer(ConnectionId connection)
  {
    PlayerLookups.Add(connection);
    return Player;
  }

  public ConnectionId? GetConnection(PlayerId player) => null;
}

internal sealed class FakeRateLimiter : ICommandRateLimiter
{
  public bool HasTokens { get; set; } = true;

  public int Calls { get; private set; }

  public bool TryAcquire()
  {
    Calls++;
    return HasTokens;
  }
}

internal sealed class FakeCommandSink : ICommandSink
{
  public CommandAdmission Result { get; set; } = CommandAdmission.Admitted(new CommandSequence(1));

  public List<(PlayerId Player, ClientCommandId ClientCommandId, IGameCommand Command)> Calls { get; } = [];

  public CommandAdmission Admit(PlayerId player, ClientCommandId clientCommandId, IGameCommand command)
  {
    Calls.Add((player, clientCommandId, command));
    return Result;
  }
}
