using System.Collections.Concurrent;
using Rts.Networking.Admission;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
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

  /// <summary>Jugador de cada conexión; si una conexión no está acá, <see cref="GetPlayer"/> devuelve <see cref="Player"/>.</summary>
  public ConcurrentDictionary<ConnectionId, PlayerId> PlayersByConnection { get; } = new();

  private readonly object _lock = new();
  private readonly List<ConnectionId> _playerLookups = [];

  public IReadOnlyList<ConnectionId> PlayerLookups
  {
    get
    {
      lock (_lock)
      {
        return [.. _playerLookups];
      }
    }
  }

  public PlayerId? GetPlayer(ConnectionId connection)
  {
    lock (_lock)
    {
      _playerLookups.Add(connection);
    }

    return PlayersByConnection.TryGetValue(connection, out PlayerId player) ? player : Player;
  }

  /// <summary>Conexión que devuelve <see cref="GetConnection"/>; <c>null</c> simula un jugador desconectado.</summary>
  public ConnectionId? ConnectionOfPlayer { get; set; }

  public ConnectionId? GetConnection(PlayerId player) =>
      ConnectionOfPlayer ?? PlayersByConnection.Where(pair => pair.Value == player).Select(pair => (ConnectionId?)pair.Key).FirstOrDefault();
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

  private readonly object _lock = new();
  private readonly List<(PlayerId Player, ClientCommandId ClientCommandId, IGameCommand Command)> _calls = [];

  /// <summary>Los receptores de cada conexión lo llaman a la vez, por eso se protege la lista.</summary>
  public IReadOnlyList<(PlayerId Player, ClientCommandId ClientCommandId, IGameCommand Command)> Calls
  {
    get
    {
      lock (_lock)
      {
        return [.. _calls];
      }
    }
  }

  public CommandAdmission Admit(PlayerId player, ClientCommandId clientCommandId, IGameCommand command)
  {
    lock (_lock)
    {
      _calls.Add((player, clientCommandId, command));
    }

    return Result;
  }
}

internal sealed class FakeViolationTracker : IViolationTracker
{
  private readonly List<ErrorCode> _registered = [];

  /// <summary>Lo que devuelve <see cref="Register"/>; verdadero simula que se superó el límite de faltas.</summary>
  public bool LimitExceeded { get; set; }

  public IReadOnlyList<ErrorCode> Registered => _registered;

  public int RecentCount => _registered.Count;

  public bool Register(ErrorCode violation)
  {
    _registered.Add(violation);
    return LimitExceeded;
  }
}
