using Rts.GameSession.Events;
using Rts.GameSession.Model;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession;

// DECIDIDO (D-082): GameSession se separa en tres piezas para que la composición sea lineal ("buzones primero"):
// el buzón y la vista son pasivos, sin dependencias, y se crean antes que Networking y Simulation; el consumidor se crea al final.

/// <summary>
/// Buzón de sesión (07 §7): la única entrada de eventos. Networking deposita conexiones, Ready y cierres; Simulation, su resultado;
/// el propio host, sus avisos internos. Solo encola: nunca bloquea al que llama ni muta estado.
/// <list type="bullet">
/// <item>Acotado por <c>EventQueueCapacity</c> solo para Hello y Ready (<c>TryPost*</c> devuelve <c>false</c> si está lleno).</item>
/// <item>Cierres de conexión, resultados de Simulation y avisos internos <b>nunca se descartan</b> (D-059).</item>
/// <item>Varios productores, un solo consumidor (<see cref="ISessionHost"/>).</item>
/// </list>
/// </summary>
public interface ISessionInbox : ISessionEventSink, IMatchResultSink
{
    /// <summary>Aviso interno del host (timer, detención, inicialización, drenaje, apagado). Nunca se descarta.</summary>
    void PostInternal(SessionEvent sessionEvent);

    /// <summary>Espera el próximo evento. Solo lo llama el consumidor.</summary>
    ValueTask<SessionEvent> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Vista de la sesión: la última instantánea confirmada, que Networking lee desde sus hilos por <see cref="ISessionGate"/>.
/// Solo el consumidor la reemplaza, con una escritura atómica de referencia; los lectores nunca ven un estado a medias.
/// </summary>
public interface ISessionStateStore : ISessionGate
{
    GameSessionState Current { get; }

    /// <summary>Publica el estado nuevo después de cada transición. Solo lo llama el consumidor.</summary>
    void Publish(GameSessionState state);
}

/// <summary>
/// Consumidor único de la sesión (07 §7). Lee el buzón de a un evento, aplica <see cref="ISessionStateMachine"/>, publica el estado
/// en la vista y ejecuta las acciones en orden.
/// <list type="bullet">
/// <item>No bloquea esperando a Simulation ni a Networking: <c>StopSimulation</c> y <c>ShutdownConnections</c> corren aparte y vuelven
/// al buzón como eventos (<c>SimulationStopped</c>, <c>ShutdownCompleted</c>).</item>
/// <item>Los plazos de Ready usan <see cref="TimeProvider"/> (reloj monotónico), no ticks.</item>
/// <item>No toma locks sobre WorldState ni llama a módulos de dominio.</item>
/// </list>
/// Se construye con el buzón, la vista, la máquina de estados, <see cref="ISessionEffects"/>, <see cref="ISimulationControl"/>,
/// la configuración y un <see cref="TimeProvider"/>.
/// </summary>
public interface ISessionHost
{
    /// <summary>
    /// Ejecuta la sesión hasta Ended y devuelve el resultado oficial. Cancelar el token equivale a pedir el apagado
    /// (<see cref="ShutdownRequested"/>, D-084): la sesión termina de forma ordenada, sin cortar en seco.
    /// </summary>
    Task<MatchResult> RunAsync(CancellationToken cancellationToken);
}
