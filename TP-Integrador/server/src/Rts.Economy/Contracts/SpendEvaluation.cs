using Rts.Shared.Errors;
using Rts.Shared.Resources;

namespace Rts.Economy.Contracts;

/// <summary>
/// Resultado de evaluar un gasto contra un saldo, sin mutar nada (01 §6). Simulation lo usa para confirmar
/// atómicamente <c>Build</c>, el inicio de <c>RepairFarm</c> y el inicio de un entrenamiento (01 §5).
/// </summary>
public sealed record SpendEvaluation
{
    private SpendEvaluation(bool isAffordable, ResourceAmount balanceAfter, ErrorCode? error)
    {
        IsAffordable = isAffordable;
        BalanceAfter = balanceAfter;
        Error = error;
    }

    public bool IsAffordable { get; }

    /// <summary>Saldo resultante si se confirma el gasto; igual al saldo original si no alcanza.</summary>
    public ResourceAmount BalanceAfter { get; }

    /// <summary>Motivo del rechazo (<see cref="EconomyErrors"/>); nulo si alcanza.</summary>
    public ErrorCode? Error { get; }

    public static SpendEvaluation Affordable(ResourceAmount balanceAfter)
    {
        if (!balanceAfter.IsNonNegative)
        {
            throw new ArgumentException("Un gasto aprobado no puede dejar saldo negativo (01 §8).", nameof(balanceAfter));
        }

        return new SpendEvaluation(true, balanceAfter, null);
    }

    public static SpendEvaluation Insufficient(ResourceAmount currentBalance, ErrorCode error)
    {
        if (error.IsDefault)
        {
            throw new ArgumentException("El rechazo requiere un código de error.", nameof(error));
        }

        return new SpendEvaluation(false, currentBalance, error);
    }
}
