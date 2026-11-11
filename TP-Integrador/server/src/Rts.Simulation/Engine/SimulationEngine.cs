using Rts.Shared.Collections;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Initialization;
using Rts.Simulation.State;

namespace Rts.Simulation.Engine;

/// <summary>
/// Núcleo sincrónico de Simulation (contrato 06 §4): ejecuta un tick completo por llamada, recorriendo un pipeline de fases.
/// <list type="bullet">
/// <item>Cada fase trabaja sobre el mismo <see cref="TickContext"/>; la publicación (fase 9) cierra el tick.</item>
/// <item><b>Atomicidad (D-044):</b> <see cref="Current"/> solo se reemplaza si todas las fases terminaron. Si alguna lanza una
/// excepción, el tick se descarta completo: <see cref="Current"/> sigue siendo el anterior, no se publica nada y el resultado
/// informa la fase que falló.</item>
/// <item>Después de un resultado de gameplay o de una falla, no ejecuta más ticks hasta un nuevo <see cref="InitializeMatch"/>.</item>
/// </list>
/// No hace I/O: devuelve la publicación en <see cref="TickOutcome"/> y el loop la entrega a State Sync.
/// </summary>
public sealed class SimulationEngine : ISimulationEngine
{
  private readonly InitialWorldFactory _initialWorld;
  private readonly IReadOnlyList<ITickPhase> _phases;
  private readonly TickRate _tickRate;
  private readonly TimeProvider _time;
  private bool _halted;

  internal SimulationEngine(InitialWorldFactory initialWorld, IReadOnlyList<ITickPhase> phases, TickRate tickRate, TimeProvider time)
  {
    _initialWorld = initialWorld ?? throw new ArgumentNullException(nameof(initialWorld));
    _phases = phases ?? throw new ArgumentNullException(nameof(phases));
    _tickRate = tickRate ?? throw new ArgumentNullException(nameof(tickRate));
    _time = time ?? throw new ArgumentNullException(nameof(time));
  }

  public WorldState? Current { get; private set; }

  public WorldView InitializeMatch()
  {
    Current = _initialWorld.Create();
    _halted = false;
    return WorldViewProjector.Project(Current);
  }

  public TickOutcome AdvanceTick()
  {
    var context = new TickContext(EnsureCanAdvance());
    var run = new PhaseRun(_time);
    try
    {
      foreach (ITickPhase phase in _phases)
      {
        run.Measure(phase.Phase, () => phase.Execute(context));
      }

      TickPublication publication = run.Measure(SimulationPhase.Publication, () => TickPublicationBuilder.Build(context));
      Current = context.World;
      _halted = context.World.IsGameplayOver;
      return new TickOutcome(publication, context.World.GameplayResult, Failure: null, BuildMetrics(context, run, publication.Events.Count));
    }
    catch (Exception ex)
    {
      _halted = true;
      var failure = new SimulationFailure(run.CurrentPhase, $"{ex.GetType().Name}: {ex.Message}");
      return new TickOutcome(Publication: null, GameplayResult: null, failure, BuildMetrics(context, run, eventsPublished: 0));
    }
  }

  private WorldState EnsureCanAdvance()
  {
    if (Current is null)
    {
      throw new InvalidOperationException("La partida no está inicializada: falta InitializeMatch.");
    }

    if (_halted)
    {
      throw new InvalidOperationException("La partida terminó (resultado o falla crítica): no se ejecutan más ticks.");
    }

    return Current;
  }

  private TickMetrics BuildMetrics(TickContext context, PhaseRun run, int eventsPublished)
  {
    TimeSpan total = run.Elapsed;
    CommandCounters commands = context.Commands;
    return new TickMetrics(
        context.Tick, total, run.Timings, context.BarrierWait, commands.Dequeued, commands.Accepted, commands.Rejected,
        commands.DuplicatesReplied, eventsPublished, IsOverrun: total > _tickRate.TickInterval);
  }

  /// <summary>Mide cada fase del tick y recuerda cuál está corriendo, para informarla si falla.</summary>
  private sealed class PhaseRun(TimeProvider time)
  {
    private readonly long _start = time.GetTimestamp();
    private readonly List<PhaseTiming> _timings = [];

    public SimulationPhase CurrentPhase { get; private set; } = SimulationPhase.CommandBatch;

    public TimeSpan Elapsed => time.GetElapsedTime(_start);

    public EquatableArray<PhaseTiming> Timings => [.. _timings];

    public void Measure(SimulationPhase phase, Action action) => Measure(phase, () =>
    {
      action();
      return true;
    });

    public T Measure<T>(SimulationPhase phase, Func<T> action)
    {
      CurrentPhase = phase;
      long phaseStart = time.GetTimestamp();
      T result = action();
      _timings.Add(new PhaseTiming(phase, time.GetElapsedTime(phaseStart)));
      return result;
    }
  }
}
