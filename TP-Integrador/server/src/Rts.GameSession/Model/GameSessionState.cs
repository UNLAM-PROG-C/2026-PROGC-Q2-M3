using Rts.Shared.Collections;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession.Model;

/// <summary>
/// Estado completo de la sesión (07 §8). Inmutable: cada transición produce uno nuevo, y GameSession es su único escritor
/// (07 §1). El constructor rechaza los estados imposibles, entre ellos "WaitingForPlayers con ambas plazas ocupadas" (07 §4).
/// <para>Invariantes por estado:</para>
/// <list type="bullet">
/// <item>WaitingForPlayers: a lo sumo una plaza, conectada y sin Ready; sin inicialización vigente ni resultado.</item>
/// <item>Starting: ambas plazas conectadas; inicialización vigente igual a la última emitida; sin resultado.</item>
/// <item>Running: ambas plazas listas; sin resultado. Un jugador desconectado figura en <see cref="PendingDisconnects"/>.
/// <see cref="StopRequested"/> es verdadero si y solo si hay desconexiones pendientes (D-055) o un apagado pedido (D-084).</item>
/// <item>Ending y Ended: resultado fijado y sin desconexiones pendientes.</item>
/// </list>
/// </summary>
public sealed record GameSessionState
{
  public GameSessionState(
      SessionId session,
      SessionStatus status,
      PlayerSession? player1,
      PlayerSession? player2,
      InitializationId? currentInitialization,
      InitializationId? lastInitialization,
      bool stopRequested,
      EquatableArray<PlayerId> pendingDisconnects,
      bool shutdownRequested,
      MatchResult? result)
  {
    List<string> errors = Validate(status, player1, player2, currentInitialization, lastInitialization, stopRequested, pendingDisconnects, shutdownRequested, result);
    if (errors.Count > 0)
    {
      throw new ArgumentException("Estado de sesión inválido: " + string.Join(" ", errors));
    }

    Session = session;
    Status = status;
    Player1 = player1;
    Player2 = player2;
    CurrentInitialization = currentInitialization;
    LastInitialization = lastInitialization;
    StopRequested = stopRequested;
    PendingDisconnects = pendingDisconnects;
    ShutdownRequested = shutdownRequested;
    Result = result;
  }

  public SessionId Session { get; }

  public SessionStatus Status { get; }

  /// <summary>Plaza P1, si está ocupada.</summary>
  public PlayerSession? Player1 { get; }

  /// <summary>Plaza P2, si está ocupada.</summary>
  public PlayerSession? Player2 { get; }

  /// <summary>Inicialización vigente: la que espera Ready (Starting) o la que arrancó la partida (Running y después).</summary>
  public InitializationId? CurrentInitialization { get; }

  /// <summary>Última inicialización emitida; la próxima será su <c>Next()</c> (D-010). Nunca se reutiliza un valor.</summary>
  public InitializationId? LastInitialization { get; }

  /// <summary>
  /// Running: GameSession ya pidió detener los ticks, por una desconexión (D-055) o un apagado (D-084), y espera que termine el
  /// tick en curso.
  /// </summary>
  public bool StopRequested { get; }

  /// <summary>Lote de desconexiones observadas desde el pedido de detención, ordenado por PlayerId (D-055).</summary>
  public EquatableArray<PlayerId> PendingDisconnects { get; }

  /// <summary>Running: el operador pidió apagar; el resultado se fija al terminar los ticks, si no hay otro (D-084).</summary>
  public bool ShutdownRequested { get; }

  /// <summary>Resultado oficial; se fija una sola vez (07 §6).</summary>
  public MatchResult? Result { get; }

  /// <summary>Verdadero solo en Running antes del pedido de detención; lo publica <see cref="ISessionGate.AcceptsGameplay"/>.</summary>
  public bool AcceptsGameplay => Status == SessionStatus.Running && !StopRequested;

  /// <summary>Próxima inicialización a emitir.</summary>
  public InitializationId NextInitialization => LastInitialization?.Next() ?? InitializationId.First;

  /// <summary>Plazas ocupadas, P1 primero.</summary>
  public IEnumerable<PlayerSession> OccupiedSlots
  {
    get
    {
      if (Player1 is not null)
      {
        yield return Player1;
      }

      if (Player2 is not null)
      {
        yield return Player2;
      }
    }
  }

  /// <summary>Sesión recién creada: esperando jugadores, sin plazas ni inicializaciones.</summary>
  public static GameSessionState Initial(SessionId session) =>
      new(session, SessionStatus.WaitingForPlayers, null, null, null, null, false, default, false, null);

  /// <summary>Plaza de un jugador, si está ocupada.</summary>
  public PlayerSession? GetSlot(PlayerId player) => player == PlayerId.One ? Player1 : player == PlayerId.Two ? Player2 : null;

  /// <summary>Plaza registrada para una conexión, si la hay (aunque ya figure desconectada).</summary>
  public PlayerSession? FindByConnection(ConnectionId connection) => OccupiedSlots.FirstOrDefault(s => s.Connection == connection);

  private static List<string> Validate(
      SessionStatus status,
      PlayerSession? player1,
      PlayerSession? player2,
      InitializationId? current,
      InitializationId? last,
      bool stopRequested,
      EquatableArray<PlayerId> pending,
      bool shutdownRequested,
      MatchResult? result)
  {
    var errors = new List<string>();
    PlayerSession[] slots = new[] { player1, player2 }.OfType<PlayerSession>().ToArray();

    if (!Enum.IsDefined(status))
    {
      errors.Add($"Estado desconocido ({(int)status}).");
    }

    if (player1 is not null && player1.Player != PlayerId.One)
    {
      errors.Add("La plaza P1 debe tener PlayerId 1.");
    }

    if (player2 is not null && player2.Player != PlayerId.Two)
    {
      errors.Add("La plaza P2 debe tener PlayerId 2.");
    }

    if (player1 is not null && player2 is not null && player1.Connection == player2.Connection)
    {
      errors.Add("Una conexión no puede ocupar ambas plazas.");
    }

    if (current is { } c && (last is not { } l || c.CompareTo(l) > 0))
    {
      errors.Add("La inicialización vigente no puede ser posterior a la última emitida.");
    }

    if (pending.Any(p => !p.IsValid) || pending.Distinct().Count() != pending.Count || !pending.SequenceEqual(pending.Order()))
    {
      errors.Add("Las desconexiones pendientes deben ser PlayerIds válidos, únicos y ordenados.");
    }

    bool isTerminalPhase = status is SessionStatus.Ending or SessionStatus.Ended;
    if (isTerminalPhase != (result is not null))
    {
      errors.Add("Solo Ending y Ended tienen resultado, y siempre lo tienen.");
    }

    if (status != SessionStatus.Running && (stopRequested || pending.Count > 0 || shutdownRequested))
    {
      errors.Add("Solo Running puede tener una detención pedida, desconexiones pendientes o un apagado pendiente.");
    }

    switch (status)
    {
      case SessionStatus.WaitingForPlayers:
        if (slots.Length > 1)
        {
          errors.Add("WaitingForPlayers con ambas plazas ocupadas es un estado imposible: debe pasar a Starting.");
        }

        if (slots.Any(s => !s.IsConnected || s.IsReady))
        {
          errors.Add("En WaitingForPlayers las plazas están conectadas y sin Ready.");
        }

        if (current is not null)
        {
          errors.Add("WaitingForPlayers no tiene inicialización vigente.");
        }

        break;

      case SessionStatus.Starting:
        if (slots.Length != 2 || slots.Any(s => !s.IsConnected))
        {
          errors.Add("Starting exige ambas plazas conectadas.");
        }

        if (current is null || current != last)
        {
          errors.Add("Starting exige que la inicialización vigente sea la última emitida.");
        }

        break;

      case SessionStatus.Running:
        if (slots.Length != 2 || slots.Any(s => !s.IsReady && s.IsConnected))
        {
          errors.Add("Running exige ambas plazas y que las conectadas estén listas.");
        }

        if (current is null)
        {
          errors.Add("Running exige la inicialización que arrancó la partida.");
        }

        PlayerId[] disconnected = slots.Where(s => !s.IsConnected).Select(s => s.Player).Order().ToArray();
        if (!disconnected.SequenceEqual(pending))
        {
          errors.Add("En Running, los desconectados son exactamente las desconexiones pendientes.");
        }

        if (stopRequested != (pending.Count > 0 || shutdownRequested))
        {
          errors.Add("La detención de los ticks se pide si y solo si hay una desconexión o un apagado pendiente.");
        }

        break;
    }

    return errors;
  }
}
