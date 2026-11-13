using Rts.Construction;
using Rts.Construction.Model;
using Rts.Movement;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>Build</c> (03 §3). Hay un handler por tipo de comando, y Build tiene dos formas, así que este deriva según cuál llegó:
/// <list type="bullet">
/// <item><b>Continuar</b> (D-032): sumar trabajadores a una obra propia que ya existe. Sin costo.</item>
/// <item><b>Colocar</b>: crear una obra nueva. Todavía no está implementada (paso 16g): se rechaza con <c>UnsupportedCommand</c>.</item>
/// </list>
/// </summary>
internal sealed class BuildCommandHandler : ICommandHandler
{
  private readonly IConstructionRules _construction;
  private readonly IMovementRules _movement;

  public BuildCommandHandler(IConstructionRules construction, IMovementRules movement)
  {
    _construction = construction ?? throw new ArgumentNullException(nameof(construction));
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
  }

  public CommandType Type => CommandType.Build;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var build = (BuildCommand)command.Command;
    return build.ExistingBuildingId is { } siteId
        ? Continue(command, build, siteId, context)
        : CommandResult.Rejected(command, context.Tick, SimulationErrors.UnsupportedCommand); // Colocar: paso 16g.
  }

  /// <summary>
  /// Forma "continuar" (03 §3):
  /// <list type="number">
  /// <item>Construction (<see cref="IConstructionRules.EvaluateContinueBuild"/>): la obra existe, es del jugador y sigue sin terminar.</item>
  /// <item>Cada trabajador, con <see cref="TaskOrderPlan"/>: propiedad y Movement (ruta en rango de la huella, mapa actual).</item>
  /// <item>Los aceptados reciben la orden Build. El progreso de la obra no cambia: lo suma Construction en la fase 3.</item>
  /// </list>
  /// </summary>
  private CommandResult Continue(CommandEnvelope command, BuildCommand build, EntityId siteId, TickContext context)
  {
    WorldState world = context.World;
    world.Buildings.TryGetValue(siteId, out BuildingEntity? site);
    ErrorCode? error = _construction.EvaluateContinueBuild(command.Player, ConstructionTargetOf(site));
    if (error is { } invalidTarget)
    {
      return CommandResult.Rejected(command, context.Tick, invalidTarget);
    }

    EnsureOwnSiteUnderConstruction(site, siteId, command.Player);
    var order = new TaskOrder(UnitOrderKind.Build, siteId, world.MovementTargetOf(siteId));
    TaskOrderPlan plan = TaskOrderPlan.Evaluate(
        world, command.Player, order, build.Routes, context.Navigation, _movement, _ => null); // Construction ya validó la obra.

    plan.CommitTo(context);
    return plan.ToResult(command, context.Tick);
  }

  /// <summary>Lo que Construction necesita saber del edificio: dueño, tipo y si ya está terminado. Nulo si no existe.</summary>
  private static ConstructionTargetInfo? ConstructionTargetOf(BuildingEntity? building) => building is null
      ? null
      : new ConstructionTargetInfo(building.Id, building.Owner, building.Type, building.IsCompleted);

  /// <summary>Si Construction admitió algo que no es una obra propia en curso, la respuesta es incoherente: falla crítica.</summary>
  private static void EnsureOwnSiteUnderConstruction(BuildingEntity? site, EntityId siteId, PlayerId player)
  {
    if (site is null || site.Owner != player || site.IsCompleted)
    {
      throw new InvalidOperationException($"Construction admitió continuar {siteId}, que no es una obra en curso de {player}.");
    }
  }
}
