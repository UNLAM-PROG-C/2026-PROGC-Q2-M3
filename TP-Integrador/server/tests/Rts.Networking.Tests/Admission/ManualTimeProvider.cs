namespace Rts.Networking.Tests.Admission;

/// <summary>
/// Reloj de pruebas: el tiempo solo avanza cuando la prueba lo pide con <see cref="Advance"/>, así no hace falta esperar de verdad.
/// El repositorio no trae un paquete de reloj falso, por eso se escribe acá.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
  private long _timestamp;

  public override long TimestampFrequency => TimeSpan.TicksPerSecond;

  public override long GetTimestamp() => _timestamp;

  public void Advance(TimeSpan amount) => _timestamp += amount.Ticks;
}
