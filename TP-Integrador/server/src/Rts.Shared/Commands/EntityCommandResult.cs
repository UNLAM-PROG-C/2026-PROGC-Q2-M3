using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Shared.Commands;

/// <summary>
/// Resultado individual de una entidad dentro de un comando grupal (04 §2, 06 §4).
/// <see cref="Error"/> nulo significa aceptada; una entidad rechazada conserva su orden anterior.
/// </summary>
public sealed record EntityCommandResult(EntityId Entity, ErrorCode? Error)
{
  public bool IsAccepted => Error is null;

  public static EntityCommandResult Accepted(EntityId entity) => new(entity, null);

  public static EntityCommandResult Rejected(EntityId entity, ErrorCode error) => new(entity, error);
}
