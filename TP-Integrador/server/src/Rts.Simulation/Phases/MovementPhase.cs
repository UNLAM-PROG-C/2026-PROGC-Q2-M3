using Rts.Movement;
using Rts.Movement.Contracts;
using Rts.Movement.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>
/// Fase 2 (06 §4, 04 §5-6). Simulation no calcula el movimiento: le pasa a Movement una foto del mundo y aplica lo que propone.
/// <list type="number">
/// <item>Arma <see cref="MovementTickInput"/> desde <c>W</c>: el mapa vigente, todas las unidades y la geometría de los objetivos.</item>
/// <item>Llama a <see cref="IMovementCalculator.CalculateTick"/> (función pura de Movement).</item>
/// <item>Verifica que la propuesta sea coherente y la confirma de inmediato: unidades actualizadas y eventos en el orden propuesto.</item>
/// </list>
/// Una propuesta incoherente es un fallo crítico: la excepción hace que el motor descarte el tick.
/// </summary>
internal sealed class MovementPhase : ITickPhase
{
  private readonly IMovementCalculator _calculator;

  public MovementPhase(IMovementCalculator calculator)
  {
    ArgumentNullException.ThrowIfNull(calculator);
    _calculator = calculator;
  }

  public SimulationPhase Phase => SimulationPhase.Movement;

  public void Execute(TickContext context)
  {
    WorldState world = context.World;
    var input = new MovementTickInput(
        context.Tick,
        context.Navigation,
        [.. world.Units.Values.Select(unit => unit.ToMovementState())],
        [.. CollectTargets(world)]);

    MovementTickResult result = _calculator.CalculateTick(input);
    if (result.Tick != context.Tick)
    {
      throw new InvalidOperationException($"Movement respondió el tick {result.Tick} en lugar de {context.Tick}.");
    }

    Apply(context, result);
  }

  /// <summary>
  /// Geometría de cada entidad apuntada por una orden de tarea, sin repetir. Unidad o árbol ⇒ un punto; edificio ⇒ su huella.
  /// Si el objetivo ya no existe, no se incluye: Movement termina esa orden con <c>TargetLost</c>.
  /// </summary>
  private static IEnumerable<MovementTarget> CollectTargets(WorldState world) => world.Units.Values
      .Select(unit => unit.Order.TargetId)
      .OfType<EntityId>()
      .Distinct()
      .Order()
      .Select(world.MovementTargetOf)
      .OfType<MovementTarget>();

  private static void Apply(TickContext context, MovementTickResult result)
  {
    var units = context.World.Units;
    foreach (UnitMovementState update in result.UnitUpdates)
    {
      if (!units.TryGetValue(update.UnitId, out UnitEntity? unit))
      {
        throw new InvalidOperationException($"Movement propuso un cambio para la unidad inexistente {update.UnitId}.");
      }

      units = units.SetItem(update.UnitId, unit.WithMovement(update));
    }

    context.World = context.World.With(units: units);
    foreach (IDomainEventPayload proposed in result.ProposedEvents)
    {
      context.Emit(proposed);
    }
  }
}
