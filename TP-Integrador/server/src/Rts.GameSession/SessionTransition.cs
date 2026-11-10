using Rts.GameSession.Actions;
using Rts.GameSession.Model;
using Rts.Shared.Collections;

namespace Rts.GameSession;

/// <summary>Resultado de aplicar un evento: el nuevo estado y las acciones, en orden de ejecución.</summary>
public sealed record SessionTransition(GameSessionState State, EquatableArray<SessionAction> Actions)
{
    /// <summary>El evento no tuvo efecto (por ejemplo, un timeout de una inicialización ya cancelada).</summary>
    public static SessionTransition Unchanged(GameSessionState state) => new(state, EquatableArray<SessionAction>.Empty);
}
