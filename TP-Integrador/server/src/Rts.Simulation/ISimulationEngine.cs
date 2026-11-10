using Rts.Shared.Match;
using Rts.Shared.World;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.State;

namespace Rts.Simulation;

/// <summary>
/// Resultado de ejecutar un tick. Exactamente uno de estos casos:
/// <list type="bullet">
/// <item>Tick confirmado: <see cref="Publication"/> presente; <see cref="GameplayResult"/> presente si la partida terminó en este tick.</item>
/// <item>Falla crítica: <see cref="Failure"/> presente; no se publica nada y el mundo queda en el último tick confirmado.</item>
/// </list>
/// </summary>
public sealed record TickOutcome(
    TickPublication? Publication,
    MatchResult? GameplayResult,
    SimulationFailure? Failure,
    TickMetrics Metrics);

/// <summary>
/// Núcleo **sincrónico** de Simulation: ejecuta un tick completo (fases 1 a 9) en el hilo que lo llama. El loop lo invoca a
/// 20 Hz y las pruebas lo invocan directamente, con fakes de los módulos y de la cola. Contrato:
/// <list type="bullet">
/// <item>Un solo hilo llama a <see cref="AdvanceTick"/>; nunca hay dos ticks a la vez (06 §3).</item>
/// <item>El tick es atómico: o se confirma completo (nuevo <see cref="Current"/>) o se descarta completo ante una falla
/// crítica (D-044, contrato 06 §4).</item>
/// <item>No hace I/O de red: entrega la publicación a <c>ITickPublisher</c>, que no bloquea.</item>
/// <item>Después de un resultado de gameplay o de una falla crítica, no ejecuta más ticks.</item>
/// </list>
/// </summary>
public interface ISimulationEngine
{
    /// <summary>Último estado confirmado; nulo antes de inicializar.</summary>
    WorldState? Current { get; }

    /// <summary>Crea el mundo inicial (tick 0) desde el mapa y la configuración, y devuelve su vista.</summary>
    WorldView InitializeMatch();

    /// <summary>Ejecuta un tick completo.</summary>
    /// <exception cref="InvalidOperationException">Si no se inicializó o la partida ya terminó.</exception>
    TickOutcome AdvanceTick();
}
