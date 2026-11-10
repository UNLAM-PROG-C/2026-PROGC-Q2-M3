using Rts.Shared.Errors;

namespace Rts.Combat;

/// <summary>Códigos de error del área Combat (D-007). Para objetivos inexistentes se usa <c>Common.EntityNotFound</c>.</summary>
public static class CombatErrors
{
    /// <summary>El objetivo no es atacable (p. ej. un árbol).</summary>
    public static readonly ErrorCode InvalidTarget = new("Combat.InvalidTarget");

    /// <summary>El objetivo pertenece al mismo jugador: no hay fuego amigo (02 §8).</summary>
    public static readonly ErrorCode FriendlyTarget = new("Combat.FriendlyTarget");
}
