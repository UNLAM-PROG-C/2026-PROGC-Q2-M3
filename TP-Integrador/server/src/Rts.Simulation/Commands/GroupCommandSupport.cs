using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>Reglas comunes a los comandos que mandan órdenes a un grupo de unidades (04 §3, 06 §4).</summary>
internal static class GroupCommandSupport
{
  /// <summary>Primer chequeo de cada unidad: que exista y sea del jugador que manda el comando. Nulo si pasa.</summary>
  public static ErrorCode? CheckOwnedUnit(WorldState world, PlayerId player, EntityId unitId)
  {
    if (!world.Units.TryGetValue(unitId, out UnitEntity? unit))
    {
      return CommonErrors.EntityNotFound;
    }

    return unit.Owner == player ? null : CommonErrors.NotOwner;
  }

  /// <summary>
  /// Resultado global a partir de los resultados por unidad (invariantes de <see cref="CommandResult"/>, contrato 00 §5):
  /// todas aceptadas ⇒ <c>Accepted</c>; ninguna ⇒ <c>Rejected</c> con <c>Common.NoEntityAccepted</c>; si no, <c>PartiallyAccepted</c>.
  /// </summary>
  public static CommandResult ToResult(CommandEnvelope command, TickId tick, EquatableArray<EntityCommandResult> results)
  {
    bool anyAccepted = results.Any(result => result.IsAccepted);
    bool anyRejected = results.Any(result => !result.IsAccepted);
    if (!anyAccepted)
    {
      return CommandResult.Rejected(command, tick, CommonErrors.NoEntityAccepted, results);
    }

    return anyRejected ? CommandResult.PartiallyAccepted(command, tick, results) : CommandResult.Accepted(command, tick, results);
  }
}
