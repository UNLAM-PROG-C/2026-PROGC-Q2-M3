using Rts.Shared.Ids;
using Rts.Shared.World;

namespace Rts.Shared.Match;

/// <summary>
/// Control de Simulation que usa GameSession (07 §3, 06 §2), sin referenciar el proyecto Simulation.
/// La implementación vive en Rts.Simulation y la composición del servidor la conecta.
/// </summary>
public interface ISimulationControl
{
    /// <summary>
    /// Crea el <c>WorldState</c> inicial a partir del mapa y la configuración, durante <c>Starting</c>, y devuelve su vista
    /// inmutable para el InitialState. Puede llamarse de nuevo en una nueva preparación, reemplazando el mundo anterior.
    /// No corre ticks.
    /// </summary>
    WorldView InitializeMatch();

    /// <summary>Comienza el loop de ticks (al pasar a Running, después de los dos Ready válidos).</summary>
    void StartTicks();

    /// <summary>
    /// Detiene el loop: termina el tick en curso, si lo hay, y no ejecuta más. Es idempotente. GameSession lo pide ante la
    /// primera desconexión en Running o al fijar un resultado (D-055). Devuelve el último tick confirmado
    /// (<see cref="TickId.Zero"/> si no corrió ninguno), que GameSession usa como <c>FinalTick</c> del resultado.
    /// </summary>
    Task<TickId> StopTicksAsync();
}

/// <summary>
/// Receptor del resultado de gameplay o de una falla crítica de Simulation. Lo implementa GameSession, que lo serializa
/// con sus demás eventos de sesión (07 §7). La implementación no debe bloquear: solo encola el aviso.
/// </summary>
public interface IMatchResultSink
{
    void OnSimulationResult(MatchResult result);
}
