using Rts.GameSession.Model;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession;

/// <summary>
/// Implementación de <see cref="ISessionStateStore"/>: la última instantánea confirmada de la sesión (07 §8, D-082).
/// <list type="bullet">
/// <item>Solo el consumidor (<see cref="ISessionHost"/>) la reemplaza, con <b>una única escritura de referencia</b>.</item>
/// <item>Como <see cref="GameSessionState"/> es inmutable, los lectores de Networking nunca ven un estado a medias ni necesitan locks.</item>
/// <item>Cada consulta lee la referencia <b>una sola vez</b>: la respuesta sale de un único estado consistente.</item>
/// </list>
/// </summary>
public sealed class SessionStateStore : ISessionStateStore
{
  private GameSessionState _current;

  /// <param name="initial">Estado de arranque (<see cref="GameSessionState.Initial"/>).</param>
  public SessionStateStore(GameSessionState initial)
  {
    ArgumentNullException.ThrowIfNull(initial);
    _current = initial;
  }

  /// <inheritdoc/>
  public GameSessionState Current => Volatile.Read(ref _current);

  /// <inheritdoc/>
  public SessionStatus Status => Current.Status;

  /// <inheritdoc/>
  public bool AcceptsGameplay => Current.AcceptsGameplay;

  /// <inheritdoc/>
  public PlayerId? GetPlayer(ConnectionId connection)
  {
    PlayerSession? slot = Current.FindByConnection(connection);
    return slot is { IsConnected: true } ? slot.Player : null;
  }

  /// <inheritdoc/>
  public ConnectionId? GetConnection(PlayerId player)
  {
    PlayerSession? slot = Current.GetSlot(player);
    return slot is { IsConnected: true } ? slot.Connection : null;
  }

  /// <inheritdoc/>
  public void Publish(GameSessionState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    Volatile.Write(ref _current, state);
  }
}
