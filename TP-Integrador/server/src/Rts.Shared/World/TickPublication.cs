using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Match;

namespace Rts.Shared.World;

/// <summary>
/// Todo lo confirmado en un tick, que Simulation entrega a State Sync en la fase 9 (06 §3, §9). Solo contiene lo ya
/// confirmado (nunca propuestas).
/// <list type="bullet">
/// <item><see cref="Events"/>: eventos de dominio en su orden oficial (contrato 06 §5).</item>
/// <item><see cref="CommandResults"/>: resultados de los comandos procesados en el tick, incluidas las respuestas repetidas a
/// duplicados (para el emisor).</item>
/// <item><see cref="Positions"/>: posiciones oficiales de todas las unidades vivas al cierre del tick. State Sync decide la cadencia de 10 Hz.</item>
/// <item><see cref="GameplayResult"/>: presente si en este tick terminó la partida por destrucción de bases.</item>
/// </list>
/// </summary>
public sealed record TickPublication(
    TickId Tick,
    MapRevision MapRevision,
    EquatableArray<DomainEvent> Events,
    EquatableArray<CommandResult> CommandResults,
    EquatableArray<UnitPosition> Positions,
    MatchResult? GameplayResult);

/// <summary>
/// Receptor de publicaciones de Simulation. Lo implementa State Sync. **No debe bloquear ni hacer I/O**: solo encola la
/// publicación en su propio flujo ordenado (06 §6, 09 §10).
/// </summary>
public interface ITickPublisher
{
  void Publish(TickPublication publication);
}
