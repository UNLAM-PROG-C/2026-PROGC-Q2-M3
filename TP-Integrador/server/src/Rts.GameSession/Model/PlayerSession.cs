using Rts.Shared.Ids;

namespace Rts.GameSession.Model;

/// <summary>
/// Plaza ocupada por una conexión (07 §8). Solo identificadores y confirmaciones: ningún objeto de socket.
/// DECIDIDO (D-054): el <see cref="Player"/> de una conexión no cambia mientras la conexión vive.
/// </summary>
public sealed record PlayerSession
{
    public PlayerSession(PlayerId player, ConnectionId connection, bool isConnected, bool isReady)
    {
        if (!player.IsValid)
        {
            throw new ArgumentException($"PlayerId inválido: {player}.", nameof(player));
        }

        if (isReady && !isConnected)
        {
            throw new ArgumentException("Un jugador desconectado no puede figurar como listo.", nameof(isReady));
        }

        Player = player;
        Connection = connection;
        IsConnected = isConnected;
        IsReady = isReady;
    }

    public PlayerId Player { get; }

    public ConnectionId Connection { get; }

    /// <summary>Falso solo en Running, Ending o Ended: antes de Running una desconexión libera la plaza (07 §2).</summary>
    public bool IsConnected { get; }

    /// <summary>Confirmó <c>ClientReady</c> para la inicialización vigente.</summary>
    public bool IsReady { get; }

    /// <summary>Recién registrado tras un Hello válido: conectado y sin Ready.</summary>
    public static PlayerSession Registered(PlayerId player, ConnectionId connection) => new(player, connection, isConnected: true, isReady: false);
}
