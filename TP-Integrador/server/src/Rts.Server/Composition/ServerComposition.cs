using Microsoft.Extensions.Logging;
using Rts.Server.Configuration;
using Rts.Shared.Match;
using Rts.Shared.World;

namespace Rts.Server.Composition;

/// <summary>
/// Crea y conecta todas las piezas, en un orden lineal sin ciclos de construcción (DECIDIDO D-082, "buzones primero"), y ejecuta
/// una partida. Composición manual con <c>new</c> (DECIDIDO D-081): si falta una dependencia, no compila.
/// <para>
/// Las piezas pasivas, que solo reciben (cola de comandos, buzón y vista de sesión), se crean primero. Así quien después necesita
/// mandarles algo ya las encuentra creadas, y ningún objeto se conecta "más tarde".
/// </para>
/// </summary>
internal static class ServerComposition
{
  /// <summary>
  /// Ejecuta una partida completa y devuelve su resultado oficial. Cancelar el token pide el apagado ordenado (D-084).
  /// </summary>
  public static Task<MatchResult> RunMatchAsync(ServerConfig config, GameCatalog catalog, ILoggerFactory loggers, CancellationToken shutdown)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(catalog);
    ArgumentNullException.ThrowIfNull(loggers);

    // Orden de construcción (contrato 10 §4). Cada paso solo usa lo creado en pasos anteriores:
    //
    // 1. Cola global de comandos (Simulation): ICommandSink para Networking, ICommandSource para Simulation.
    //      var commands = new CommandQueue(config.Simulation.CommandQueueCapacity);
    //
    // 2. Buzón y vista de sesión (GameSession): pasivos, sin dependencias.
    //      var inbox = new SessionInbox(config.GameSession.EventQueueCapacity);
    //      var sessionView = new SessionStateStore(GameSessionState.Initial(SessionId.New()));
    //
    // 3. Networking: deposita en el buzón, lee la vista y admite en la cola.
    //      var network = new NetworkServer(config.Networking, inbox, sessionView, commands, TimeProvider.System, loggers);
    //
    // 4. State Sync: proyecta y encola en las colas de salida de Networking.
    //      var stateSync = new StateSyncPublisher(new StateProjector(config.StateSync, config.Simulation.TickRate), network, catalog);
    //
    // 5. Simulation: el mapa, el motor con los cinco módulos de dominio y el loop de 20 Hz. Publica en State Sync y avisa su
    //    resultado al buzón.
    //      MapDefinition map = MapLoader.Load(config.MapFile, <tamaño del Centro Principal según Construction>);
    //      var modules = new DomainModules(<reglas y calculadores de los cinco módulos de dominio>);
    //      ISimulationEngine engine = SimulationFactory.CreateEngine(
    //          config.Simulation, map, EconomyParameters.FromConfig(config.Economy), commands, modules, TimeProvider.System);
    //      ISimulationLoop simulation = SimulationFactory.CreateLoop(engine, config.Simulation, stateSync, inbox, TimeProvider.System);
    //
    // 6. Consumidor de sesión (GameSession): el único que necesita a casi todos, por eso va al final.
    //      var effects = new SessionEffectsAdapter(network, stateSync);
    //      var session = new SessionHost(inbox, sessionView, new SessionStateMachine(config.GameSession), effects, simulation,
    //                                    config.GameSession, TimeProvider.System, loggers);
    //
    // Arranque y fin (contrato 10 §5):
    //      Task listening = network.RunAsync(shutdown);       // aceptación en segundo plano
    //      MatchResult result = await session.RunAsync(shutdown); // hasta Ended: ya detuvo ticks y drenó conexiones
    //      await listening;                                    // termina al completar el drenaje
    //      return result;
    throw new NotImplementedException(
        "La composición necesita las implementaciones de los módulos (cola de comandos, buzón y vista de sesión, NetworkServer, "
        + "StateSyncPublisher, SimulationEngine/Loop, SessionHost). Se completa en la vertical slice (AGENTS §8).");
  }
}
