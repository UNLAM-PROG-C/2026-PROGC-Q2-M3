using Rts.Shared.Commands;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.State;

namespace Rts.Simulation.Engine;

/// <summary>
/// Estado de trabajo de **un** tick. Lo crea el motor al empezar el tick, lo pasan de fase en fase y se descarta al terminar.
/// Solo lo usa el hilo de Simulation, así que es mutable y sin locks; lo que sale hacia afuera (la publicación) es inmutable.
/// <list type="bullet">
/// <item><see cref="Previous"/> es <c>S</c>, el último mundo confirmado; nunca cambia.</item>
/// <item><see cref="World"/> es <c>W</c>, el mundo en construcción: cada fase lo reemplaza por uno nuevo (D-044).</item>
/// <item><see cref="Emit"/> sella cada evento con el tick y su índice, en el orden en que las fases los confirman (contrato 06 §5).</item>
/// </list>
/// </summary>
internal sealed class TickContext
{
  private readonly List<DomainEvent> _events = [];
  private readonly List<CommandResult> _commandResults = [];

  public TickContext(WorldState previous)
  {
    ArgumentNullException.ThrowIfNull(previous);
    Previous = previous;
    Tick = previous.Tick.Next();
    World = previous.With(tick: Tick);
  }

  /// <summary>Tick que se está calculando.</summary>
  public TickId Tick { get; }

  public WorldState Previous { get; }

  public WorldState World { get; set; }

  public IReadOnlyList<DomainEvent> Events => _events;

  public IReadOnlyList<CommandResult> CommandResults => _commandResults;

  public CommandCounters Commands { get; } = new();

  /// <summary>Tiempo que la fase 5 esperó en la barrera Economy ∥ Combat (métrica, 06 §10).</summary>
  public TimeSpan BarrierWait { get; set; }

  /// <summary>Confirma un evento: le asigna el sello <c>(Tick, índice)</c> siguiente.</summary>
  public void Emit(IDomainEventPayload payload)
  {
    ArgumentNullException.ThrowIfNull(payload);
    _events.Add(new DomainEvent(new EventStamp(Tick, _events.Count), payload));
  }

  public void AddCommandResult(CommandResult result)
  {
    ArgumentNullException.ThrowIfNull(result);
    _commandResults.Add(result);
  }
}

/// <summary>Contadores de comandos del tick, para las métricas (06 §10).</summary>
internal sealed class CommandCounters
{
  public int Dequeued { get; set; }

  public int Accepted { get; set; }

  public int Rejected { get; set; }

  public int DuplicatesReplied { get; set; }
}
