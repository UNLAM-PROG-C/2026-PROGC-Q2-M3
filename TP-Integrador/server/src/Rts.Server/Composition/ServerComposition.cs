using Microsoft.Extensions.Logging;
using Rts.Combat;
using Rts.Construction;
using Rts.Economy;
using Rts.GameSession;
using Rts.GameSession.Model;
using Rts.Movement;
using Rts.Networking;
using Rts.Server.Configuration;
using Rts.Server.Diagnostics;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.World;
using Rts.Simulation;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Map;
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
  /// <param name="map">El mapa ya cargado y validado por <c>Program</c>, junto con la configuración (contrato 10 §1).</param>
  public static Task<MatchResult> RunMatchAsync(
      ServerConfig config, MapDefinition map, GameCatalog catalog, ILoggerFactory loggers, CancellationToken shutdown)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(map);
    ArgumentNullException.ThrowIfNull(catalog);
    ArgumentNullException.ThrowIfNull(loggers);

    // Orden de construcción (contrato 10 §4). Cada paso solo usa lo creado en pasos anteriores.
    TransportPieces transport = CreateTransport(config, catalog, loggers);           // Pasos 1 a 4.
    SimulationPieces simulation = CreateSimulation(config, map, transport, loggers); // Paso 5.

    // PENDIENTE: lo que sigue todavía es borrador (las variables sueltas pasan a ser las de transport y simulation).
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
    _ = simulation;
    throw new NotImplementedException(
        "La composición todavía no crea el host de sesión (paso 6). Se completa en la vertical slice (AGENTS §8).");
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

  /// <summary>
  /// Paso 5 (contrato 10 §4): el motor con los cinco módulos de dominio, que lee comandos de la cola, y el loop de 20 Hz alrededor,
  /// que publica cada tick en State Sync, avisa el fin de la partida al buzón y entrega sus métricas al log (D-095, D-100).
  /// </summary>
  internal static SimulationPieces CreateSimulation(
      ServerConfig config, MapDefinition map, TransportPieces transport, ILoggerFactory loggers)
  {
    ISimulationEngine engine = SimulationFactory.CreateEngine(
        config.Simulation,
        map,
        EconomyParameters.FromConfig(config.Economy),
        transport.Commands,
        CreateDomainModules(config),
        TimeProvider.System);

    var diagnostics = new LoggingSimulationDiagnostics(loggers, config.Simulation);
    ISimulationLoop loop = SimulationFactory.CreateLoop(
        engine, config.Simulation, transport.StateSync, transport.Inbox, diagnostics, TimeProvider.System);

    return new SimulationPieces(loop, diagnostics);
  }

  /// <summary>
  /// Las reglas y calculadores de los cinco módulos de dominio (D-090), cada uno con su propia sección de la configuración.
  /// Production es provisorio hasta que exista el módulo real (<see cref="UnavailableProduction"/>).
  /// </summary>
  internal static DomainModules CreateDomainModules(ServerConfig config)
  {
    var movementRules = new MovementRules(config.Movement, config.Simulation.TickRate);
    var production = new UnavailableProduction(config.Production);
    return new DomainModules(
        movementRules,
        new MovementCalculator(movementRules),
        new EconomyRules(config.Economy),
        new EconomyCalculator(config.Economy),
        new CombatRules(config.Combat),
        new CombatCalculator(config.Combat),
        new ConstructionRules(config.Construction),
        new ConstructionCalculator(config.Construction),
        production,
        production);
  }
}
