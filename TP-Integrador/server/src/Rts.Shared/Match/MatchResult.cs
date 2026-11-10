using Rts.Shared.Ids;

namespace Rts.Shared.Match;

/// <summary>Tipo de resultado final de la partida (07 §5).</summary>
public enum MatchResultType
{
    /// <summary>Hay ganador.</summary>
    Victory = 1,

    /// <summary>Empate: ambos Centros Principales cayeron en el mismo tick.</summary>
    Draw = 2,

    /// <summary>Finalización técnica sin ganador.</summary>
    TechnicalEnd = 3,
}

/// <summary>Motivo de fin de partida (07 §5).</summary>
public enum MatchEndReason
{
    /// <summary>Cayó solo el Centro Principal de un jugador (lo decide Simulation).</summary>
    BaseDestroyed = 1,

    /// <summary>Cayeron ambos Centros Principales en el mismo tick (lo decide Simulation).</summary>
    BothBasesDestroyed = 2,

    /// <summary>Un jugador se desconectó durante Running; gana el que permanece (lo decide GameSession).</summary>
    PlayerDisconnected = 3,

    /// <summary>Ambos se desconectaron en el mismo lote de sesión antes de fijar resultado (lo decide GameSession).</summary>
    BothPlayersDisconnected = 4,

    /// <summary>Falla crítica que impide garantizar la consistencia (06 §7).</summary>
    CriticalError = 5,

    /// <summary>El operador apagó el servidor (Ctrl+C o señal del sistema) sin otro resultado (lo decide GameSession, D-084).</summary>
    ServerShutdown = 6,
}

/// <summary>
/// Resultado oficial de la partida; se fija una sola vez (07 §6). El constructor verifica las combinaciones válidas:
/// <list type="bullet">
/// <item>Victory: ganador presente; motivo <c>BaseDestroyed</c> o <c>PlayerDisconnected</c>.</item>
/// <item>Draw: sin ganador; motivo <c>BothBasesDestroyed</c>.</item>
/// <item>TechnicalEnd: sin ganador; motivo <c>BothPlayersDisconnected</c>, <c>CriticalError</c> o <c>ServerShutdown</c>.</item>
/// </list>
/// </summary>
public sealed record MatchResult
{
    public MatchResult(MatchResultType resultType, PlayerId? winner, MatchEndReason reason, TickId? finalTick)
    {
        bool valid = resultType switch
        {
            MatchResultType.Victory => winner is { IsValid: true } && reason is MatchEndReason.BaseDestroyed or MatchEndReason.PlayerDisconnected,
            MatchResultType.Draw => winner is null && reason == MatchEndReason.BothBasesDestroyed,
            MatchResultType.TechnicalEnd => winner is null
                && reason is MatchEndReason.BothPlayersDisconnected or MatchEndReason.CriticalError or MatchEndReason.ServerShutdown,
            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException($"Resultado incoherente: {resultType}, ganador={winner?.ToString() ?? "ninguno"}, motivo={reason}.");
        }

        ResultType = resultType;
        Winner = winner;
        Reason = reason;
        FinalTick = finalTick;
    }

    public MatchResultType ResultType { get; }

    public PlayerId? Winner { get; }

    public MatchEndReason Reason { get; }

    /// <summary>Último tick de gameplay confirmado, si lo hubo.</summary>
    public TickId? FinalTick { get; }

    public static MatchResult BaseDestroyed(PlayerId winner, TickId tick) => new(MatchResultType.Victory, winner, MatchEndReason.BaseDestroyed, tick);

    public static MatchResult Draw(TickId tick) => new(MatchResultType.Draw, null, MatchEndReason.BothBasesDestroyed, tick);

    public static MatchResult CriticalError(TickId? lastConfirmedTick) => new(MatchResultType.TechnicalEnd, null, MatchEndReason.CriticalError, lastConfirmedTick);

    /// <summary>Victoria por desconexión del rival durante Running (lo decide GameSession, D-055).</summary>
    public static MatchResult PlayerDisconnected(PlayerId winner, TickId? lastConfirmedTick) =>
        new(MatchResultType.Victory, winner, MatchEndReason.PlayerDisconnected, lastConfirmedTick);

    /// <summary>Final técnico: ambos se desconectaron en el mismo lote de cierre (lo decide GameSession, D-055).</summary>
    public static MatchResult BothPlayersDisconnected(TickId? lastConfirmedTick) =>
        new(MatchResultType.TechnicalEnd, null, MatchEndReason.BothPlayersDisconnected, lastConfirmedTick);

    /// <summary>Final técnico por apagado del servidor a pedido del operador (lo decide GameSession, D-084).</summary>
    public static MatchResult ServerShutdown(TickId? lastConfirmedTick) =>
        new(MatchResultType.TechnicalEnd, null, MatchEndReason.ServerShutdown, lastConfirmedTick);
}
