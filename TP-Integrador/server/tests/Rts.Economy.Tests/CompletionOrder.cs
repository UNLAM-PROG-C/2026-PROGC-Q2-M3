using System.Collections.Concurrent;

namespace Rts.Economy.Tests;

/// <summary>
/// Fuerza qué subcálculo termina primero cuando corren en paralelo: el que debe terminar segundo no empieza su trabajo
/// hasta que el primero terminó. Registra el orden real de finalización para poder comprobarlo.
/// </summary>
internal sealed class CompletionOrder
{
  private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

  private readonly ManualResetEventSlim _firstFinished = new(initialState: false);
  private readonly ConcurrentQueue<string> _finished = new();

  public IReadOnlyList<string> Finished => [.. _finished];

  public T Run<T>(string name, bool finishesFirst, Func<T> work) =>
      finishesFirst ? RunThenSignal(name, work) : WaitThenRun(name, work);

  private T RunThenSignal<T>(string name, Func<T> work)
  {
    T result = work();
    _finished.Enqueue(name);
    _firstFinished.Set();
    return result;
  }

  private T WaitThenRun<T>(string name, Func<T> work)
  {
    if (!_firstFinished.Wait(MaxWait))
    {
      throw new TimeoutException("El subcálculo que debía terminar primero nunca terminó: ¿corrieron en secuencia?");
    }

    T result = work();
    _finished.Enqueue(name);
    return result;
  }
}
