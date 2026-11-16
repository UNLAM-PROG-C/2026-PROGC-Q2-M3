namespace Rts.Simulation.Loop;

/// <summary>
/// Agenda de ticks del loop (06 §6, D-047). Lógica pura, sin reloj ni hilos: recibe el tiempo transcurrido desde que arrancó
/// el loop y decide cuándo empieza el próximo tick.
/// <list type="bullet">
/// <item><b>Sin deriva:</b> cada tick está agendado en <c>anterior + intervalo</c>, no en <c>"ahora" + intervalo</c>. Si el sistema
/// operativo despierta tarde al hilo, ese retraso no se acumula en los ticks siguientes.</item>
/// <item><b>Sobrecarga:</b> si al terminar un tick ya pasó la hora del siguiente, el próximo arranca de inmediato y la agenda se
/// <b>re-ancla</b> en "ahora". No hay ráfagas para recuperar los ticks perdidos.</item>
/// </list>
/// </summary>
internal sealed class TickSchedule
{
  private readonly TimeSpan _interval;

  public TickSchedule(TimeSpan interval)
  {
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
    _interval = interval;
  }

  /// <summary>Momento (desde que arrancó el loop) en que debe empezar el próximo tick. El primero empieza enseguida.</summary>
  public TimeSpan NextStart { get; private set; } = TimeSpan.Zero;

  /// <summary>Registra que terminó el tick agendado en <see cref="NextStart"/> y agenda el siguiente.</summary>
  /// <returns>Verdadero si hubo sobrecarga (el tick se pasó de su intervalo).</returns>
  public bool TickFinished(TimeSpan now)
  {
    TimeSpan planned = NextStart + _interval;
    if (now > planned)
    {
      NextStart = now;
      return true;
    }

    NextStart = planned;
    return false;
  }

  /// <summary>Cuánto falta para el próximo tick; cero si ya es la hora.</summary>
  public TimeSpan DelayUntilNextStart(TimeSpan now) => NextStart > now ? NextStart - now : TimeSpan.Zero;
}
