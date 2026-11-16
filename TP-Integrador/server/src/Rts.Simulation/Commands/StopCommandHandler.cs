using System.Collections.Immutable;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>Stop</c> (04 §3): cancela la orden activa de cada unidad y la deja en reposo (Idle), sin revertir progreso ni recursos.
/// <list type="bullet">
/// <item>Cada unidad se evalúa por separado: inexistente ⇒ <c>Common.EntityNotFound</c>; ajena ⇒ <c>Common.NotOwner</c>.</item>
/// <item>Una unidad que ya estaba en reposo se acepta, pero no emite evento porque su orden no cambió.</item>
/// <item>Las aceptadas se confirman juntas y emiten <see cref="UnitOrderChanged"/> en el orden del comando.</item>
/// </list>
/// </summary>
internal sealed class StopCommandHandler : ICommandHandler
{
  public CommandType Type => CommandType.Stop;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var stop = (StopCommand)command.Command;
    var results = new List<EntityCommandResult>();
    var units = context.World.Units;
    var stopped = new List<EntityId>();

    foreach (EntityId unitId in stop.UnitIds)
    {
      ErrorCode? error = GroupCommandSupport.CheckOwnedUnit(context.World, command.Player, unitId);
      if (error is { } rejection)
      {
        results.Add(EntityCommandResult.Rejected(unitId, rejection));
        continue;
      }

      results.Add(EntityCommandResult.Accepted(unitId));
      UnitEntity unit = units[unitId];
      if (!unit.Order.IsIdle)
      {
        units = units.SetItem(unitId, unit.AsIdle());
        stopped.Add(unitId);
      }
    }

    Commit(context, units, stopped);
    return GroupCommandSupport.ToResult(command, context.Tick, [.. results]);
  }

  private static void Commit(TickContext context, ImmutableSortedDictionary<EntityId, UnitEntity> units, List<EntityId> stopped)
  {
    if (stopped.Count == 0)
    {
      return;
    }

    context.World = context.World.With(units: units);
    foreach (EntityId unitId in stopped)
    {
      context.Emit(new UnitOrderChanged(unitId, OrderId.None, UnitOrderKind.Idle));
    }
  }
}
