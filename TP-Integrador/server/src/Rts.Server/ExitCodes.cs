using Rts.Shared.Match;

namespace Rts.Server;

/// <summary>Códigos de salida del proceso (DECIDIDO D-058; detalle RECOMENDADO D-085).</summary>
public static class ExitCodes
{
    /// <summary>La partida terminó con un resultado oficial que no es error, o <c>--validate-only</c> sin problemas.</summary>
    public const int Success = 0;

    /// <summary>La partida terminó por <see cref="MatchEndReason.CriticalError"/>.</summary>
    public const int CriticalError = 1;

    /// <summary>Configuración ilegible o inválida: el servidor no arrancó.</summary>
    public const int InvalidConfiguration = 2;

    /// <summary>Argumentos de línea de comandos inválidos.</summary>
    public const int InvalidArguments = 3;

    /// <summary>TEMPORAL: la composición todavía no tiene implementaciones que instanciar (contrato 10 §5).</summary>
    public const int NotYetComposed = 4;

    /// <summary>Código de salida para un resultado oficial.</summary>
    public static int FromResult(MatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Reason == MatchEndReason.CriticalError ? CriticalError : Success;
    }
}
