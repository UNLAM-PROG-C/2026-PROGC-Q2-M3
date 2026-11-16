using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.World;
using Rts.Simulation.State;

namespace Rts.Simulation.Engine;

/// <summary>
/// Fase 9 (contrato 06 §4): cierra el tick y arma lo que se entrega a State Sync.
/// <list type="bullet">
/// <item>Emite <see cref="ResourcesChanged"/> para cada jugador cuyo saldo cambió respecto del tick anterior: P1 y después P2,
/// al final de todos los eventos (D-048).</item>
/// <item>Arma la <see cref="TickPublication"/> con los eventos en su orden oficial, los resultados de comandos y las posiciones
/// de todas las unidades vivas (09 §11).</item>
/// </list>
/// No reemplaza <c>Current</c>: eso lo hace el motor después, solo si todo el tick salió bien.
/// </summary>
internal static class TickPublicationBuilder
{
  public static TickPublication Build(TickContext context)
  {
    EmitResourceChanges(context);
    WorldState world = context.World;
    return new TickPublication(
        context.Tick,
        world.MapRevision,
        [.. context.Events],
        [.. context.CommandResults],
        [.. world.Units.Values.Select(unit => new UnitPosition(unit.Id, unit.Position))],
        world.GameplayResult);
  }

  private static void EmitResourceChanges(TickContext context)
  {
    foreach ((PlayerId player, PlayerState state) in context.World.Players)
    {
      if (state.Resources != context.Previous.Players[player].Resources)
      {
        context.Emit(new ResourcesChanged(player, state.Resources));
      }
    }
  }
}
