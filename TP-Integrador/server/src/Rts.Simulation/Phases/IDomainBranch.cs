using Rts.Combat.Contracts;
using Rts.Economy.Contracts;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>
/// Una de las dos ramas de la fase paralela (06 §4, fases 5 y 6): Economy o Combat. Separa lo que corre en paralelo de lo que no:
/// <list type="bullet">
/// <item><see cref="Calculate"/> corre en <b>otra tarea</b>. Solo puede leer el snapshot inmutable que recibe: arma la entrada del
/// módulo y llama a su calculador. Nunca toca el <see cref="TickContext"/>, que no es seguro entre hilos.</item>
/// <item><see cref="Commit"/> corre en el <b>hilo de Simulation</b>, después de la barrera: verifica la coherencia del resultado y lo
/// aplica sobre <c>W</c>.</item>
/// </list>
/// </summary>
internal interface IDomainBranch<TResult>
    where TResult : class
{
  TResult Calculate(WorldState snapshot, TickId tick);

  void Commit(TickContext context, TResult result);
}

/// <summary>El trabajo lanzado en la fase 5, que la fase 6 espera en la barrera.</summary>
internal sealed record DomainWork(Task<EconomyTickResult> Economy, Task<CombatTickResult> Combat);
