using Rts.Combat.Contracts;
using Rts.Economy.Contracts;
using Rts.Shared.Ids;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>Fase 4 (06 §4): fija el snapshot. Es <c>W</c> mismo, sin copia: al ser inmutable, nadie puede cambiarlo mientras se lee.</summary>
internal sealed class SnapshotPhase : ITickPhase
{
  public SimulationPhase Phase => SimulationPhase.Snapshot;

  public void Execute(TickContext context) => context.Snapshot = context.World;
}

/// <summary>
/// Fase 5 (06 §4, D-051): lanza Economy y Combat sobre el snapshot.
/// <list type="bullet">
/// <item><b>En paralelo</b> (lo aprobado): una tarea del pool para cada rama. Cada tarea recibe solo el snapshot y el número de tick,
/// nunca el <see cref="TickContext"/>.</item>
/// <item><b>En secuencia</b> (solo para medir y comparar en la defensa): las dos en el hilo de Simulation, primero Economy.</item>
/// </list>
/// No espera nada: la espera es la barrera de la fase 6.
/// </summary>
internal sealed class EconomyCombatLaunchPhase : ITickPhase
{
  private readonly IDomainBranch<EconomyTickResult> _economy;
  private readonly IDomainBranch<CombatTickResult> _combat;
  private readonly bool _runInParallel;

  public EconomyCombatLaunchPhase(
      IDomainBranch<EconomyTickResult> economy,
      IDomainBranch<CombatTickResult> combat,
      bool runInParallel)
  {
    _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    _combat = combat ?? throw new ArgumentNullException(nameof(combat));
    _runInParallel = runInParallel;
  }

  public SimulationPhase Phase => SimulationPhase.EconomyAndCombat;

  public void Execute(TickContext context)
  {
    WorldState snapshot = context.Snapshot ?? throw new InvalidOperationException("Falta el snapshot de la fase 4.");
    TickId tick = context.Tick;

    context.DomainWork = _runInParallel
        ? new DomainWork(
            Task.Run(() => _economy.Calculate(snapshot, tick)),
            Task.Run(() => _combat.Calculate(snapshot, tick)))
        : new DomainWork(
            Task.FromResult(_economy.Calculate(snapshot, tick)),
            Task.FromResult(_combat.Calculate(snapshot, tick)));
  }
}

/// <summary>
/// Fase 6 (06 §4): la <b>barrera</b>. Espera <b>las dos</b> ramas con <c>BarrierTimeout</c> y recién entonces confirma, siempre
/// <b>primero Economy y después Combat</b>. Como ambas leyeron el mismo snapshot y el orden de confirmación es fijo, el resultado no
/// depende de cuál terminó primero.
/// <para>
/// Plazo vencido o rama con error ⇒ excepción ⇒ el motor descarta el tick completo. Una tarea abandonada por el plazo puede seguir
/// corriendo, pero es inofensiva: solo lee un snapshot inmutable y su resultado ya no se usa.
/// </para>
/// </summary>
internal sealed class BarrierAndCommitPhase : ITickPhase
{
  private const string EconomyName = "Economy";
  private const string CombatName = "Combat";

  private readonly IDomainBranch<EconomyTickResult> _economy;
  private readonly IDomainBranch<CombatTickResult> _combat;
  private readonly TimeSpan _barrierTimeout;
  private readonly TimeProvider _time;

  public BarrierAndCommitPhase(
      IDomainBranch<EconomyTickResult> economy,
      IDomainBranch<CombatTickResult> combat,
      TimeSpan barrierTimeout,
      TimeProvider time)
  {
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(barrierTimeout, TimeSpan.Zero);
    _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    _combat = combat ?? throw new ArgumentNullException(nameof(combat));
    _barrierTimeout = barrierTimeout;
    _time = time ?? throw new ArgumentNullException(nameof(time));
  }

  public SimulationPhase Phase => SimulationPhase.BarrierAndCommit;

  public void Execute(TickContext context)
  {
    DomainWork work = context.DomainWork ?? throw new InvalidOperationException("La fase 5 no lanzó Economy ni Combat.");
    context.DomainWork = null;

    long waitStartedAt = _time.GetTimestamp();
    bool finished = WaitWithoutThrowing(Task.WhenAll(work.Economy, work.Combat), _barrierTimeout);
    context.BarrierWait = _time.GetElapsedTime(waitStartedAt);
    if (!finished)
    {
      throw new TimeoutException($"Economy ∥ Combat no terminó dentro de la barrera ({_barrierTimeout}).");
    }

    EconomyTickResult economy = ResultOf(work.Economy, EconomyName);
    CombatTickResult combat = ResultOf(work.Combat, CombatName);

    _economy.Commit(context, economy); // Orden fijo (06 §4): "la comida se acredita y después se destruye".
    _combat.Commit(context, combat);
  }

  /// <summary>Verdadero si la tarea terminó (bien o con error) dentro del plazo; los errores se informan después, por rama.</summary>
  private static bool WaitWithoutThrowing(Task task, TimeSpan timeout)
  {
    try
    {
      return task.Wait(timeout);
    }
    catch (AggregateException)
    {
      return true;
    }
  }

  private static T ResultOf<T>(Task<T> task, string branch)
  {
    if (task.Exception?.InnerException is { } error)
    {
      throw new InvalidOperationException($"{branch} falló: {error.GetType().Name}: {error.Message}", error);
    }

    return task.Result;
  }
}
