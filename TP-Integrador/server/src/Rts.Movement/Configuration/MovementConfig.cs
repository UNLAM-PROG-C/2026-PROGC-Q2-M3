using Rts.Shared.Entities;

namespace Rts.Movement.Configuration;

/// <summary>
/// Parámetros de movimiento tipados (04 §10). Distancias en unidades de mundo (= metros de Godot, D-029),
/// velocidades por segundo (se convierten por tick con <c>TickRate</c>) y plazos en ticks. Valores PENDIENTES de balance.
/// </summary>
public sealed record MovementConfig
{
    /// <summary>Velocidad máxima por tipo de unidad, en unidades de mundo por segundo.</summary>
    public required IReadOnlyDictionary<UnitType, double> SpeedPerSecond { get; init; }

    /// <summary>Distancia máxima entre el final de la ruta de un Move y su destino.</summary>
    public required double ArrivalTolerance { get; init; }

    /// <summary>Rango de interacción con un árbol para recolectar.</summary>
    public required double GatherRange { get; init; }

    /// <summary>Rango de interacción con la huella de un edificio para construir o reparar.</summary>
    public required double BuildRange { get; init; }

    /// <summary>
    /// Rango hasta el que un soldado se acerca a su objetivo. RECOMENDADO (D-030): la composición del servidor lo fija igual a
    /// <c>CombatConfig.Soldier.Range</c> y valida esa igualdad al iniciar, para tener una sola fuente.
    /// </summary>
    public required double AttackRange { get; init; }

    /// <summary>Máximo de waypoints por ruta candidata.</summary>
    public required int MaxWaypointsPerRoute { get; init; }

    /// <summary>Ticks que una unidad en RepathPending espera una ruta válida antes de cancelar su orden.</summary>
    public required int RepathTimeoutTicks { get; init; }

    /// <summary>Mínimo de ticks entre dos pedidos de ruta por persecución de la misma unidad.</summary>
    public required int PursuitRepathIntervalTicks { get; init; }

    /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (SpeedPerSecond is null)
        {
            errors.Add($"{nameof(SpeedPerSecond)} es obligatorio.");
        }
        else
        {
            foreach (UnitType type in Enum.GetValues<UnitType>().Where(t => !SpeedPerSecond.ContainsKey(t)))
            {
                errors.Add($"Falta la velocidad de {type}.");
            }

            foreach ((UnitType type, double speed) in SpeedPerSecond.Where(kv => !IsPositiveFinite(kv.Value) || !Enum.IsDefined(kv.Key)))
            {
                errors.Add($"Velocidad inválida para {type} ({speed}).");
            }
        }

        RequirePositiveFinite(errors, ArrivalTolerance, nameof(ArrivalTolerance));
        RequirePositiveFinite(errors, GatherRange, nameof(GatherRange));
        RequirePositiveFinite(errors, BuildRange, nameof(BuildRange));
        RequirePositiveFinite(errors, AttackRange, nameof(AttackRange));
        RequirePositive(errors, MaxWaypointsPerRoute, nameof(MaxWaypointsPerRoute));
        RequirePositive(errors, RepathTimeoutTicks, nameof(RepathTimeoutTicks));
        RequirePositive(errors, PursuitRepathIntervalTicks, nameof(PursuitRepathIntervalTicks));
        return errors;
    }

    /// <summary>Valida la configuración completa.</summary>
    /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
    public void Validate()
    {
        IReadOnlyList<string> errors = GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new ArgumentException("Configuración de Movement inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static void RequirePositiveFinite(List<string> errors, double value, string name)
    {
        if (!IsPositiveFinite(value))
        {
            errors.Add($"{name} debe ser finito y positivo ({value}).");
        }
    }

    private static void RequirePositive(List<string> errors, int value, string name)
    {
        if (value <= 0)
        {
            errors.Add($"{name} debe ser positivo ({value}).");
        }
    }
}
