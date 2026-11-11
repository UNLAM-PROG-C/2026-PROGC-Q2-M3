using Rts.Shared.Commands;
using Rts.Simulation.Engine;

namespace Rts.Simulation.Commands;

/// <summary>
/// Procesa un tipo de comando en la fase 1 (patrón Strategy: un handler por <see cref="CommandType"/>).
/// Contrato del handler:
/// <list type="bullet">
/// <item>Valida con los módulos dueños y, si acepta, confirma todo el comando como <b>una sola operación</b>: reemplaza
/// <c>context.World</c> y emite sus eventos (06 §5).</item>
/// <item>Si rechaza, no toca <c>context.World</c> ni emite nada: las unidades conservan su orden anterior (04 §3).</item>
/// <item>Un rechazo de negocio se devuelve como resultado, nunca como excepción. Una excepción es una falla crítica que
/// descarta el tick.</item>
/// </list>
/// La idempotencia no es responsabilidad del handler: la resuelve <see cref="CommandBatchPhase"/> antes de llamarlo.
/// </summary>
internal interface ICommandHandler
{
  CommandType Type { get; }

  CommandResult Handle(CommandEnvelope command, TickContext context);
}
