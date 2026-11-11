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
/// Fases implementadas: 1 (lote de comandos: Stop y Move) y 2 (Movement); la 9 (publicación) la hace el motor. Las fases 3 a 8
/// y los demás handlers se agregan acá a medida que se implementan; mientras tanto, esos comandos se rechazan con
/// <c>Simulation.UnsupportedCommand</c>.
/// </para>
/// </summary>
public static class SimulationFactory
{
  /// <summary>Arma el motor sincrónico con la configuración ya validada, el mapa cargado, la cola y los módulos de dominio.</summary>
  /// <param name="maxWoodPerTree">Madera inicial de cada árbol (<c>EconomyConfig.MaxWoodPerTree</c>).</param>
  /// <exception cref="ArgumentException">Si la configuración es inválida.</exception>
  public static ISimulationEngine CreateEngine(
      SimulationConfig config,
      MapDefinition map,
      int maxWoodPerTree,
      ICommandSource commands,
      DomainModules modules,
      TimeProvider time)
  {
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(map);
    ArgumentNullException.ThrowIfNull(commands);
    ArgumentNullException.ThrowIfNull(modules);
    config.Validate();

    var initialWorld = new InitialWorldFactory(
        map, maxWoodPerTree, modules.EconomyRules, modules.ConstructionRules, modules.CombatRules);

    ICommandHandler[] handlers =
    [
      new StopCommandHandler(),
      new MoveCommandHandler(modules.MovementRules),
    ];

    ITickPhase[] phases =
    [
      new CommandBatchPhase(commands, config.MaxCommandsPerTick, handlers),
      new MovementPhase(modules.MovementCalculator),
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
