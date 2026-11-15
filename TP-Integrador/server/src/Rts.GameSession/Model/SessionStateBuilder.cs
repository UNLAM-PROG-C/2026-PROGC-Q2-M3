using Rts.Shared.Collections;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession.Model;

/// <summary>
/// Builder de <see cref="GameSessionState"/>: parte de un estado existente, cambia solo lo necesario y valida todo junto en
/// <see cref="Build"/>. Hace falta porque el estado es inmutable y su constructor verifica invariantes que dependen de varios
/// campos a la vez (por ejemplo, Running con una desconexión exige el lote y la detención pedida en el mismo paso).
/// </summary>
internal sealed class SessionStateBuilder
{
  private readonly SessionId _session;
  private readonly List<PlayerId> _pendingDisconnects;
  private SessionStatus _status;
  private PlayerSession? _player1;
  private PlayerSession? _player2;
  private InitializationId? _currentInitialization;
  private InitializationId? _lastInitialization;
  private bool _stopRequested;
  private bool _shutdownRequested;
  private MatchResult? _result;

  private SessionStateBuilder(GameSessionState state)
  {
    _session = state.Session;
    _pendingDisconnects = [.. state.PendingDisconnects];
    _status = state.Status;
    _player1 = state.Player1;
    _player2 = state.Player2;
    _currentInitialization = state.CurrentInitialization;
    _lastInitialization = state.LastInitialization;
    _stopRequested = state.StopRequested;
    _shutdownRequested = state.ShutdownRequested;
    _result = state.Result;
  }

  public static SessionStateBuilder From(GameSessionState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    return new SessionStateBuilder(state);
  }

  public SessionStateBuilder WithStatus(SessionStatus status)
  {
    _status = status;
    return this;
  }

  /// <summary>Ocupa, reemplaza o libera (<c>null</c>) la plaza de un jugador.</summary>
  public SessionStateBuilder WithSlot(PlayerId player, PlayerSession? slot)
  {
    if (player == PlayerId.One)
    {
      _player1 = slot;
    }
    else
    {
      _player2 = slot;
    }

    return this;
  }

  /// <summary>Emite una inicialización nueva: pasa a ser la vigente y la última emitida (07 §4).</summary>
  public SessionStateBuilder WithNewInitialization(InitializationId initialization)
  {
    _currentInitialization = initialization;
    _lastInitialization = initialization;
    return this;
  }

  /// <summary>Invalida la inicialización vigente; la última emitida se conserva para no reutilizar su valor.</summary>
  public SessionStateBuilder WithoutCurrentInitialization()
  {
    _currentInitialization = null;
    return this;
  }

  public SessionStateBuilder WithStopRequested(bool stopRequested)
  {
    _stopRequested = stopRequested;
    return this;
  }

  public SessionStateBuilder WithShutdownRequested(bool shutdownRequested)
  {
    _shutdownRequested = shutdownRequested;
    return this;
  }

  /// <summary>Suma un jugador al lote de desconexiones de Running (D-055).</summary>
  public SessionStateBuilder AddPendingDisconnect(PlayerId player)
  {
    _pendingDisconnects.Add(player);
    return this;
  }

  /// <summary>Fija el resultado y deja la detención resuelta: Ending y Ended no tienen nada pendiente.</summary>
  public SessionStateBuilder WithResult(MatchResult result)
  {
    _result = result;
    _stopRequested = false;
    _shutdownRequested = false;
    _pendingDisconnects.Clear();
    return this;
  }

  /// <exception cref="ArgumentException">Si la combinación viola un invariante de <see cref="GameSessionState"/>.</exception>
  public GameSessionState Build() => new(
      _session,
      _status,
      _player1,
      _player2,
      _currentInitialization,
      _lastInitialization,
      _stopRequested,
      _pendingDisconnects.Order().ToEquatableArray(),
      _shutdownRequested,
      _result);
}
