using Rts.Shared.Entities;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>
/// Fase 7 (06 §4, 02 §5): decide si la partida terminó por destrucción de bases, mirando qué Centros Principales siguen en pie
/// después del commit de Combat.
/// <list type="bullet">
/// <item>Cayó solo el de un jugador ⇒ <c>BaseDestroyed</c> y gana el otro.</item>
/// <item>Cayeron los dos en el mismo tick ⇒ <c>Draw</c>.</item>
/// </list>
/// Si hay resultado, lo fija en <c>GameplayResult</c>: el motor deja de avanzar, la fase 8 no corre y el loop lo avisa a GameSession.
/// Alcanza con ver si existe el Centro Principal, porque solo puede caer en la fase 6 y no se puede volver a construir.
/// </summary>
internal sealed class VictoryRulesPhase : ITickPhase
{
  public SimulationPhase Phase => SimulationPhase.VictoryRules;

  public void Execute(TickContext context)
  {
    WorldState world = context.World;
    if (world.IsGameplayOver)
    {
      return;
    }

    bool p1Standing = HasMainCenter(world, PlayerId.One);
    bool p2Standing = HasMainCenter(world, PlayerId.Two);
    MatchResult? result = (p1Standing, p2Standing) switch
    {
      (true, true) => null,
      (true, false) => MatchResult.BaseDestroyed(PlayerId.One, context.Tick),
      (false, true) => MatchResult.BaseDestroyed(PlayerId.Two, context.Tick),
      (false, false) => MatchResult.Draw(context.Tick),
    };

    if (result is not null)
    {
      context.World = world.With(gameplayResult: result);
    }
  }

  private static bool HasMainCenter(WorldState world, PlayerId player) =>
      world.Buildings.Values.Any(building => building.Type == BuildingType.MainCenter && building.Owner == player);
}
