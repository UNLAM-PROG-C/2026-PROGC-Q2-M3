using Microsoft.Extensions.Logging;
using Rts.GameSession;
using Rts.GameSession.Model;
using Rts.Networking;
using Rts.Server.Configuration;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.World;
using Rts.Simulation.Commands;
using Rts.StateSync;
using Rts.StateSync.Projection;

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

    // Orden de construcción (contrato 10 §4). Cada paso solo usa lo creado en pasos anteriores.
    TransportPieces transport = CreateTransport(config, catalog, loggers); // Pasos 1 a 4.

    // PENDIENTE: lo que sigue todavía es borrador (las variables sueltas pasan a ser las de transport).
    //
    // 5. Simulation: el mapa, el motor con los cinco módulos de dominio y el loop de 20 Hz. Publica en State Sync y avisa su
    //    resultado al buzón.
    //      MapDefinition map = MapLoader.Load(config.MapFile, <tamaño del Centro Principal según Construction>);
    //      var modules = new DomainModules(<reglas y calculadores de los cinco módulos de dominio>);
    //      ISimulationEngine engine = SimulationFactory.CreateEngine(
    //          config.Simulation, map, EconomyParameters.FromConfig(config.Economy), commands, modules, TimeProvider.System);
    //      var diagnostics = new LoggingSimulationDiagnostics(loggers, config.Simulation);   // D-095, D-098, D-100
    //      ISimulationLoop simulation = SimulationFactory.CreateLoop(
    //          engine, config.Simulation, stateSync, inbox, diagnostics, TimeProvider.System);
    //
    // 6. Consumidor de sesión (GameSession): el único que necesita a casi todos, por eso va al final.
    //      var effects = new SessionEffectsAdapter(network, stateSync);
    //      var session = new SessionHost(inbox, sessionView, new SessionStateMachine(config.GameSession), effects, simulation,
    //                                    config.GameSession, TimeProvider.System, loggers);
    //
    // Arranque y fin (contrato 10 §5):
    //      Task listening = network.RunAsync(shutdown);       // aceptación en segundo plano
    //      MatchResult result = await session.RunAsync(shutdown); // hasta Ended: ya detuvo ticks y drenó conexiones
    //      diagnostics.WriteFinalSummary();                    // los ticks ya están detenidos: no llegan más (D-097)
    //      await listening;                                    // termina al completar el drenaje
    //      return result;
    _ = transport;
    throw new NotImplementedException(
        "La composición todavía no crea Simulation (paso 5) ni el host de sesión (paso 6). Se completa en la vertical slice (AGENTS §8).");
  }

  /// <summary>
  /// Pasos 1 a 4 (contrato 10 §4), "buzones primero" (D-082): primero lo pasivo (cola, buzón, vista), después Networking, que
  /// deposita en esas tres, y por último State Sync, que escribe en las colas de salida de Networking. Crear no arranca nada.
  /// </summary>
  internal static TransportPieces CreateTransport(ServerConfig config, GameCatalog catalog, ILoggerFactory loggers)
  {
    // 1. Cola global de comandos (Simulation).
    var commands = new CommandQueue(config.Simulation.CommandQueueCapacity);

    // 2. Buzón y vista de sesión (GameSession): pasivos, sin dependencias. La sesión empieza esperando jugadores.
    var inbox = new SessionInbox(config.GameSession.EventQueueCapacity);
    var sessionView = new SessionStateStore(GameSessionState.Initial(SessionId.New()));

    // 3. Networking: deposita eventos en el buzón, lee la vista para saber quién es quién y admite comandos en la cola.
    var network = new NetworkServer(config.Networking, inbox, sessionView, commands, TimeProvider.System, loggers);

    // 4. State Sync: proyecta cada tick y lo encola en las colas de salida de Networking.
    var projector = new StateProjector(config.StateSync, config.Simulation.TickRate);
    var stateSync = new StateSyncPublisher(projector, network, catalog);

    return new TransportPieces(commands, inbox, sessionView, network, stateSync);
  }
}
