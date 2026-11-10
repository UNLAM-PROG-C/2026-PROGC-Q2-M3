namespace Rts.Shared.Commands;

/// <summary>
/// Payload tipado de un comando de gameplay, ya traducido desde el DTO de red.
/// RECOMENDADO (contrato 00 §5): los payloads concretos (p. ej. <c>MoveCommand</c>) se definen en
/// <c>Rts.Shared.Commands</c> al trabajar cada módulo, porque los consumen a la vez Networking (traducción),
/// Simulation (orquestación) y el módulo de dominio (validación), y ninguno de ellos debe depender de otro.
/// Deben ser records inmutables. Nunca contienen <c>PlayerId</c>: la identidad viene del <see cref="CommandEnvelope"/>.
/// </summary>
public interface IGameCommand
{
  CommandType Type { get; }
}
