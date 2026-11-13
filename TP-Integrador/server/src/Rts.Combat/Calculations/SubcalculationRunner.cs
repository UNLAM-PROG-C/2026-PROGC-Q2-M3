namespace Rts.Combat.Calculations;

/// <summary>
/// Ejecuta los dos subcálculos de Combat (soldados y torres), en secuencia o en paralelo.
/// <para>
/// Cada subcálculo devuelve su propio resultado y no escribe nada compartido, así que no hacen falta locks. Esperar la
/// tarea (<c>GetResult</c>) es la barrera: garantiza que el resultado ya está completo y visible para este hilo.
/// </para>
/// <para>
/// El modo secuencial es el predeterminado. Así, cuando Simulation corre con <c>RunDomainPhaseInParallel = false</c>
/// para medir (D-051), la fase 5 es secuencial de verdad y la comparación no mezcla dos niveles de paralelismo.
/// </para>
/// </summary>
internal static class SubcalculationRunner
{
  public static (TFirst First, TSecond Second) Run<TFirst, TSecond>(bool inParallel, Func<TFirst> first, Func<TSecond> second)
  {
    if (!inParallel)
    {
      return (first(), second());
    }

    // El primero va al thread pool y el segundo corre en este hilo: nunca quedan los dos esperando un hilo libre.
    Task<TFirst> pendingFirst = Task.Run(first);
    TSecond secondResult = second();
    return (pendingFirst.GetAwaiter().GetResult(), secondResult);
  }
}
