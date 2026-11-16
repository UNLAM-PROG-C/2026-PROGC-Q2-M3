using Rts.Combat;
using Rts.Construction;
using Rts.Economy;
using Rts.Movement;
using Rts.Production;
using Rts.Server.Configuration;
using Rts.Server.Validation;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.World;
using Rts.Simulation;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Map;
using Rts.Simulation.State;

namespace Rts.Integration.Tests.Support;

/// <summary>
/// Una partida armada como la armaría el servidor: la configuración de <c>server.example.json</c>, la cola de comandos real y
/// el motor de <see cref="SimulationFactory"/> con los cinco módulos de dominio reales (Economy, Combat, Movement,
/// Construction y Production). Los ticks se avanzan a mano (sin el loop de 20 Hz): la prueba es determinista y no espera.
/// </summary>
internal sealed class IntegrationMatch
{
  private const string ConfigFile = "server.example.json";

  private readonly CommandQueue _commands;
  private readonly ISimulationEngine _engine;
  private readonly List<TickPublication> _publications = [];
  private long _lastClientCommandId;

  private IntegrationMatch(CommandQueue commands, ISimulationEngine engine)
  {
    _commands = commands;
    _engine = engine;
  }

  public WorldState World => _engine.Current!;

  public IReadOnlyList<TickPublication> Publications => _publications;

  /// <summary>Lee la configuración de ejemplo y exige que pase la misma validación cruzada que al arrancar el servidor.</summary>
  public static ServerConfig LoadConfig()
  {
    ServerConfig config = ServerConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, ConfigFile));
    Assert.Empty(ConfigurationValidator.Validate(config));
    return config;
  }

  /// <summary>El mapa de <c>MapFile</c>, validado con la huella del Centro Principal de la configuración.</summary>
  public static MapDefinition LoadConfiguredMap(ServerConfig config) =>
      MapLoader.Load(config.MapFile, MainCenterSize(config));

  public static FootprintSize MainCenterSize(ServerConfig config) => config.Construction.FootprintSizes[BuildingType.MainCenter];

  /// <param name="runDomainPhaseInParallel">Economy ∥ Combat en paralelo (como en la config) o en secuencia, para compararlos.</param>
  public static IntegrationMatch Start(ServerConfig config, MapDefinition map, bool runDomainPhaseInParallel)
  {
    var commands = new CommandQueue(config.Simulation.CommandQueueCapacity);
    ISimulationEngine engine = SimulationFactory.CreateEngine(
        config.Simulation with { RunDomainPhaseInParallel = runDomainPhaseInParallel },
        map,
        EconomyParameters.FromConfig(config.Economy),
        commands,
        RealModules(config),
        TimeProvider.System);
    engine.InitializeMatch();
    return new IntegrationMatch(commands, engine);
  }

  /// <summary>
  /// Encola el comando como lo haría Networking; Simulation lo procesa en la fase 1 del próximo tick. Devuelve su clave, para
  /// buscar el resultado con <see cref="ResultOf"/>.
  /// </summary>
  public IdempotencyKey Submit(PlayerId player, IGameCommand command)
  {
    var key = new IdempotencyKey(player, new ClientCommandId(++_lastClientCommandId));
    CommandAdmission admission = _commands.Admit(key.Player, key.ClientCommandId, command);
    Assert.True(admission.IsAdmitted, $"La cola rechazó el comando: {admission.Error}.");
    return key;
  }

  /// <summary>El resultado publicado del comando con esa clave.</summary>
  public CommandResult ResultOf(IdempotencyKey key) =>
      _publications.SelectMany(publication => publication.CommandResults).Single(result => result.Key == key);

  /// <summary>Avanza un tick y exige que no haya falla crítica.</summary>
  public TickPublication Advance()
  {
    TickOutcome outcome = _engine.AdvanceTick();
    Assert.True(outcome.Failure is null, $"Falla crítica en el tick: {outcome.Failure}.");
    _publications.Add(outcome.Publication!);
    return outcome.Publication!;
  }

  /// <summary>
  /// Avanza <b>al menos un tick</b> (un comando recién enviado recién se procesa en el próximo) y sigue hasta que
  /// <paramref name="done"/> se cumpla; falla si no pasa en <paramref name="maxTicks"/> ticks.
  /// </summary>
  public void AdvanceUntil(Func<WorldState, bool> done, int maxTicks)
  {
    int ticks = 0;
    do
    {
      Advance();
      ticks++;
    }
    while (ticks < maxTicks && !done(World));

    Assert.True(done(World), $"La condición no se cumplió en {maxTicks} ticks (tick actual: {World.Tick}).");
  }

  public UnitEntity UnitAt(PlayerId owner, PositionXZ position) =>
      World.Units.Values.Single(unit => unit.Owner == owner && unit.Position == position);

  public BuildingEntity MainCenterOf(PlayerId owner) =>
      World.Buildings.Values.Single(building => building.Owner == owner && building.Type == BuildingType.MainCenter);

  public TreeEntity TreeAt(GridCell cell) => World.Trees.Values.Single(tree => tree.Cell == cell);

  private static DomainModules RealModules(ServerConfig config)
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
