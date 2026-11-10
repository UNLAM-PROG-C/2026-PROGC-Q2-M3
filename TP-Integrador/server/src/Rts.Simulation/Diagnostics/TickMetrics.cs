using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Simulation.Diagnostics;

/// <summary>Fases del tick, en el orden aprobado (06 §3).</summary>
public enum SimulationPhase
{
  CommandBatch = 1,
  Movement = 2,
  Construction = 3,
  Snapshot = 4,
  EconomyAndCombat = 5,
  BarrierAndCommit = 6,
  VictoryRules = 7,
  Production = 8,
  Publication = 9,
}

/// <summary>Duración medida de una fase.</summary>
public sealed record PhaseTiming(SimulationPhase Phase, TimeSpan Duration);

/// <summary>
/// Métricas de un tick (06 §10), útiles para la defensa del TP: duración total y por fase, espera en la barrera,
/// comandos (tomados, aceptados, rechazados, duplicados respondidos), eventos y si hubo sobrecarga (&gt; intervalo de tick).
/// </summary>
public sealed record TickMetrics(
    TickId Tick,
    TimeSpan Total,
    EquatableArray<PhaseTiming> Phases,
    TimeSpan BarrierWait,
    int CommandsDequeued,
    int CommandsAccepted,
    int CommandsRejected,
    int DuplicatesReplied,
    int EventsPublished,
    bool IsOverrun);

/// <summary>Falla crítica de un tick: fase y motivo (06 §7). El tick se descarta completo y la sesión termina en forma técnica.</summary>
public sealed record SimulationFailure(SimulationPhase Phase, string Reason);
