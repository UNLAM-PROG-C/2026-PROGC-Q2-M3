using Rts.Shared.Commands;
using Rts.Shared.Match;
using Rts.Shared.World;
using Rts.Simulation.Commands;
using Rts.Simulation.Configuration;
using Rts.Simulation.Engine;
using Rts.Simulation.Initialization;
using Rts.Simulation.Loop;
using Rts.Simulation.Map;
using Rts.Simulation.Phases;

namespace Rts.Simulation.Composition;

/// <summary>
/// Única puerta pública para armar Simulation con sus piezas reales (contrato 10 §4, paso 5). Es el único lugar que sabe
/// <b>qué fases</b> corren, <b>en qué orden</b> y <b>qué handler</b> atiende cada comando. El resto del código recibe todo armado.
/// <para>
/// Fases implementadas: las 9. 1 (lote de comandos: Stop, Move, TrainUnit, Attack, GatherWood, RepathResponse, RepairFarm y Build continuar), 2 (Movement), 3 (Construction), 4 a 6 (snapshot, Economy ∥ Combat
/// y barrera), 7 (Victory Rules), 8 (Production) y 9 (publicación, la hace el motor). Los demás handlers se agregan acá; mientras
/// tanto, esos comandos se rechazan con <c>Simulation.UnsupportedCommand</c>.
/// </para>
/// </summary>
public static class SimulationFactory
{
  /// <summary>Arma el motor sincrónico con la configuración ya validada, el mapa cargado, la cola y los módulos de dominio.</summary>
  /// <param name="economy">Valores de Economy que Simulation necesita para crear estado (<see cref="EconomyParameters.FromConfig"/>).</param>
  /// <exception cref="ArgumentException">Si la configuración es inválida.</exception>
  public static ISimulationEngine CreateEngine(
      SimulationConfig config,
      MapDefinition map,
      EconomyParameters economy,
      ICommandSource commands,
      DomainModules modules,
      TimeProvider time)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(map);
    ArgumentNullException.ThrowIfNull(economy);
    ArgumentNullException.ThrowIfNull(commands);
    ArgumentNullException.ThrowIfNull(modules);
    config.Validate();

    var initialWorld = new InitialWorldFactory(
        map, economy.MaxWoodPerTree, modules.EconomyRules, modules.ConstructionRules, modules.CombatRules);

    ICommandHandler[] handlers =
    [
      new StopCommandHandler(),
      new MoveCommandHandler(modules.MovementRules),
      new TrainUnitCommandHandler(modules.ProductionRules),
      new AttackCommandHandler(modules.CombatRules, modules.MovementRules),
      new GatherWoodCommandHandler(modules.EconomyRules, modules.MovementRules),
      new RepathResponseCommandHandler(modules.MovementRules),
      new RepairFarmCommandHandler(modules.ConstructionRules, modules.EconomyRules, modules.MovementRules),
      new BuildCommandHandler(modules.ConstructionRules, modules.MovementRules),
    ];

    var economyAdapter = new EconomyAdapter(modules.EconomyCalculator, modules.MovementRules);
    var combatAdapter = new CombatAdapter(modules.CombatCalculator);

    ITickPhase[] phases =
    [
      new CommandBatchPhase(commands, config.MaxCommandsPerTick, handlers),
      new MovementPhase(modules.MovementCalculator),
      new ConstructionPhase(modules.ConstructionCalculator, modules.ConstructionRules, modules.MovementRules, economy.BatchesPerFarmCycle),
      new SnapshotPhase(),
      new EconomyCombatLaunchPhase(economyAdapter, combatAdapter, config.RunDomainPhaseInParallel),
      new BarrierAndCommitPhase(economyAdapter, combatAdapter, config.BarrierTimeout, time),
      new VictoryRulesPhase(),
      new ProductionPhase(modules.ProductionCalculator, modules.EconomyRules, modules.CombatRules),
    ];

    return new SimulationEngine(initialWorld, phases, config.TickRate, time);
  }

  /// <summary>
  /// Arma el loop de 20 Hz alrededor del motor: publica cada tick en <paramref name="publisher"/> (State Sync) y avisa el fin de la
  /// partida a <paramref name="results"/> (el buzón de GameSession). Lo que recibe GameSession es este loop, como
  /// <see cref="ISimulationControl"/>.
  /// </summary>
  public static ISimulationLoop CreateLoop(
      ISimulationEngine engine,
      SimulationConfig config,
      ITickPublisher publisher,
      IMatchResultSink results,
      TimeProvider time)
  {
    ArgumentNullException.ThrowIfNull(config);
    config.Validate();
    return new SimulationLoop(engine, publisher, results, config.TickRate, config.TickStallTimeout, time);
  }
}
