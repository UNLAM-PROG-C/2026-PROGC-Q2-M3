using Rts.Combat;
using Rts.Construction;
using Rts.Economy;
using Rts.Integration.Tests.Stubs;
using Rts.Movement;
using Rts.Server.Configuration;
using Rts.Shared.Commands;
using Rts.Shared.Ids;
using Rts.Simulation;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Map;
using Rts.Simulation.State;

namespace Rts.Measurement;

/// <summary>
/// Una partida para medir: el motor de <see cref="SimulationFactory"/> con Economy, Combat, Movement y Construction reales
/// (Production es un stub porque todavía no existe) y la cola de comandos real. Los ticks se avanzan a mano, sin el loop de 20 Hz:
/// se mide el cálculo, no la espera entre ticks.
/// </summary>
internal sealed class MeasuredMatch
{
  private readonly CommandQueue _commands;
  private readonly ISimulationEngine _engine;
  private long _lastClientCommandId;

  private MeasuredMatch(CommandQueue commands, ISimulationEngine engine)
  {
    _commands = commands;
    _engine = engine;
  }

  public WorldState World => _engine.Current!;

  /// <param name="commandCapacity">
  /// Límite de comandos por tick y capacidad de la cola <b>solo para medir</b>: así todas las órdenes del escenario entran en el
  /// primer tick (que es de calentamiento). El resto de la configuración es la del servidor.
  /// </param>
  public static MeasuredMatch Start(ServerConfig config, MapDefinition map, int commandCapacity, bool runDomainPhaseInParallel)
  {
    int capacity = Math.Max(commandCapacity, config.Simulation.CommandQueueCapacity);
    var commands = new CommandQueue(capacity);
    ISimulationEngine engine = SimulationFactory.CreateEngine(
        config.Simulation with
        {
          RunDomainPhaseInParallel = runDomainPhaseInParallel,
          MaxCommandsPerTick = capacity,
          CommandQueueCapacity = capacity,
        },
        map,
        EconomyParameters.FromConfig(config.Economy),
        commands,
        AvailableModules(config),
        TimeProvider.System);
    engine.InitializeMatch();
    return new MeasuredMatch(commands, engine);
  }

  public void Submit(IEnumerable<PlayerOrder> orders)
  {
    foreach (PlayerOrder order in orders)
    {
      CommandAdmission admission = _commands.Admit(order.Player, new ClientCommandId(++_lastClientCommandId), order.Command);
      if (!admission.IsAdmitted)
      {
        throw new InvalidOperationException($"La cola rechazó una orden del escenario: {admission.Error}.");
      }
    }
  }

  /// <summary>Avanza un tick. Una falla crítica invalida la medición, así que corta todo.</summary>
  public TickOutcome Advance()
  {
    TickOutcome outcome = _engine.AdvanceTick();
    if (outcome.Failure is { } failure)
    {
      throw new InvalidOperationException($"Falla crítica durante la medición, en {failure.Phase}: {failure.Reason}", failure.Cause);
    }

    return outcome;
  }

  private static DomainModules AvailableModules(ServerConfig config)
  {
    var movementRules = new MovementRules(config.Movement, config.Simulation.TickRate);
    var production = new ProductionStub();
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
