using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Simulation.Diagnostics;

/// <summary>
/// Resumen de una ventana de ticks consecutivos (06 §10, DECIDIDO D-098): lo que se registra en nivel
/// <see cref="Configuration.DiagnosticsLevel.Summary"/> y lo que compara la medición de paralelo frente a secuencial.
/// <list type="bullet">
/// <item><see cref="AverageTotal"/>/<see cref="MaxTotal"/>: duración del cálculo del tick (si el servidor anda holgado o al límite).</item>
/// <item><see cref="AveragePhases"/>: promedio de cada fase, en orden de fase, sobre los ticks en que esa fase corrió (un tick
/// descartado por falla no llega a correr las fases siguientes).</item>
/// <item><see cref="AverageBarrierWait"/>/<see cref="MaxBarrierWait"/>: espera por Economy ∥ Combat en la barrera.</item>
/// <item><see cref="Overruns"/>: cuántas veces el juego se atrasó y la agenda se re-ancló (D-047, D-097).</item>
/// <item>Comandos y eventos: totales de la ventana.</item>
/// </list>
/// </summary>
public sealed record TickMetricsSummary(
    TickId FirstTick,
    TickId LastTick,
    int TickCount,
    TimeSpan AverageTotal,
    TimeSpan MaxTotal,
    EquatableArray<PhaseTiming> AveragePhases,
    TimeSpan AverageBarrierWait,
    TimeSpan MaxBarrierWait,
    int Overruns,
    int CommandsDequeued,
    int CommandsAccepted,
    int CommandsRejected,
    int DuplicatesReplied,
    int EventsPublished);
