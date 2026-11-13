using Rts.Shared.Commands;
using Rts.Simulation.Commands;
using Rts.Simulation.Engine;

namespace Rts.Simulation.Tests.Support;

/// <summary>
/// <see cref="CommandBatchPhase"/> exige un handler por cada <see cref="CommandType"/> (D-093). Las pruebas que solo usan algunos
/// completan el resto con <see cref="UnusedHandler"/>.
/// </summary>
internal static class TestHandlers
{
  public static ICommandHandler[] CompletedWith(params ICommandHandler[] used) =>
  [
    .. used,
    .. Enum.GetValues<CommandType>().Where(type => used.All(handler => handler.Type != type)).Select(type => new UnusedHandler(type)),
  ];
}

/// <summary>Handler de relleno: si una prueba le manda un comando, falla con un mensaje claro.</summary>
internal sealed class UnusedHandler(CommandType type) : ICommandHandler
{
  public CommandType Type => type;

  public CommandResult Handle(CommandEnvelope command, TickContext context) =>
      throw new NotSupportedException($"La prueba no esperaba un comando {type}.");
}
