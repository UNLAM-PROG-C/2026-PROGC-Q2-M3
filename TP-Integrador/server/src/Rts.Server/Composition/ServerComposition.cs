using Microsoft.Extensions.Logging;
using Rts.Combat;
using Rts.Construction;
using Rts.Economy;
using Rts.GameSession;
using Rts.GameSession.Model;
using Rts.Movement;
using Rts.Networking;
using Rts.Production;
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
  /// <exception cref="System.Net.Sockets.SocketException">Si la red no pudo escuchar (por ejemplo, el puerto está ocupado).</exception>
  public static async Task<MatchResult> RunMatchAsync(
      ServerConfig config, MapDefinition map, GameCatalog catalog, ILoggerFactory loggers, CancellationToken shutdown)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(map);
    ArgumentNullException.ThrowIfNull(catalog);
    ArgumentNullException.ThrowIfNull(loggers);

    // Orden de construcción (contrato 10 §4). Cada paso solo usa lo creado en pasos anteriores.
    TransportPieces transport = CreateTransport(config, catalog, loggers);           // Pasos 1 a 4.
    SimulationPieces simulation = CreateSimulation(config, map, transport, loggers); // Paso 5.
    ISessionHost session = CreateSession(config, transport, simulation, loggers);    // Paso 6.

    return await RunAsync(transport.Network, session, simulation.Diagnostics, shutdown);
  }

  /// <summary>
  /// Arranque y fin (contrato 10 §4): la red acepta en segundo plano mientras el host de sesión corre hasta <c>Ended</c>. Para
  /// entonces la sesión ya detuvo los ticks y drenó las conexiones, y con el drenaje la red dejó de aceptar.
  /// </summary>
  private static async Task<MatchResult> RunAsync(
      NetworkServer network, ISessionHost session, LoggingSimulationDiagnostics diagnostics, CancellationToken shutdown)
  {
    // La red NO recibe el token de apagado: si lo recibiera, el primer Ctrl+C cortaría la aceptación y el monitor de heartbeat
    // antes del drenaje ordenado. Termina sola cuando la sesión pide DrainAndCloseAllAsync, que siempre ocurre antes de Ended.
    Task listening = network.RunAsync(CancellationToken.None);

    // La sesión sí recibe el apagado (D-084), y además un segundo motivo: que la red se caiga antes de tiempo.
    using var stopSession = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
    Task<MatchResult> running = session.RunAsync(stopSession.Token);

    // Si la red falla primero (por ejemplo, el puerto está ocupado), sin red no hay partida: en vez de esperar jugadores para
    // siempre, se pide el apagado ordenado y después se propaga el error de la red.
    if (await Task.WhenAny(listening, running) == listening && listening.IsFaulted)
    {
      await stopSession.CancelAsync();
    }

    MatchResult result = await running;
    diagnostics.WriteFinalSummary(); // Los ticks ya están detenidos: no llegan más métricas (D-097, D-100).
    await listening;                 // Ya terminó con el drenaje; si había fallado, acá se relanza su error.
    return result;
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
  /// Paso 6 (contrato 10 §4): el consumidor único de la sesión. Va al final porque es el que necesita a casi todos: lee del buzón,
  /// escribe la vista, habla con la red y State Sync por el adaptador de efectos (10 §5) y controla los ticks con el loop.
  /// Recibe su <c>ILogger</c> de la fábrica de la composición (D-096, que cerró la O-011): el único que escribe es el Server.
  /// </summary>
  internal static ISessionHost CreateSession(
      ServerConfig config, TransportPieces transport, SimulationPieces simulation, ILoggerFactory loggers)
  {
    var effects = new SessionEffectsAdapter(transport.Network, transport.StateSync);
    var machine = new SessionStateMachine(config.GameSession);
    return new SessionHost(
        transport.Inbox, transport.SessionView, machine, effects, simulation.Loop, config.GameSession, TimeProvider.System,
        loggers.CreateLogger<SessionHost>());
  }

  /// <summary>
  /// Las reglas y calculadores de los cinco módulos de dominio (D-090), cada uno con su propia sección de la configuración.
  /// Production no depende de Economy (D-015): recibe acá el costo en comida de cada unidad, el mismo que Simulation verifica
  /// al cobrar cada inicio de entrenamiento (contrato 05 §5).
  /// </summary>
  internal static DomainModules CreateDomainModules(ServerConfig config)
  {
    var movementRules = new MovementRules(config.Movement, config.Simulation.TickRate);
    var productionRules = new ProductionRules(config.Production);
    return new DomainModules(
        movementRules,
        new MovementCalculator(movementRules),
        new EconomyRules(config.Economy),
        new EconomyCalculator(config.Economy),
        new CombatRules(config.Combat),
        new CombatCalculator(config.Combat),
        new ConstructionRules(config.Construction),
        new ConstructionCalculator(config.Construction),
        productionRules,
        new ProductionCalculator(productionRules, config.Economy.UnitFoodCosts));
  }
}
