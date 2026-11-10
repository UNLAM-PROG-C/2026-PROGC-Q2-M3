using Rts.Shared.Entities;

namespace Rts.Combat.Configuration;

/// <summary>
/// Estadísticas de ataque de un tipo de atacante (02 §3, §10). <see cref="Range"/> está en unidades de mundo y se compara
/// de forma inclusiva: a un punto para objetivos unidad, y al borde de la huella cuando hay un edificio involucrado (D-019, D-020).
/// </summary>
public sealed record AttackStats(int Damage, double Range, int CooldownTicks);

/// <summary>
/// Parámetros de combate tipados (02 §10). Combat es dueño de las estadísticas de ataque y del HP máximo de las
/// unidades (D-021); el HP máximo de los edificios es de Construction. Valores numéricos PENDIENTES de balance.
/// Los cooldowns están en ticks. Se valida una vez al iniciar el servidor.
/// </summary>
public sealed record CombatConfig
{
    /// <summary>HP máximo por tipo de unidad; Simulation lo usa al hacer aparecer una unidad.</summary>
    public required IReadOnlyDictionary<UnitType, int> UnitMaxHp { get; init; }

    /// <summary>Ataque cuerpo a cuerpo de los soldados.</summary>
    public required AttackStats Soldier { get; init; }

    /// <summary>Ataque automático de las torres (solo contra unidades).</summary>
    public required AttackStats Tower { get; init; }

    /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (UnitMaxHp is null)
        {
            errors.Add($"{nameof(UnitMaxHp)} es obligatorio.");
        }
        else
        {
            foreach (UnitType type in Enum.GetValues<UnitType>().Where(t => !UnitMaxHp.ContainsKey(t)))
            {
                errors.Add($"Falta el HP máximo de {type}.");
            }

            foreach ((UnitType type, int hp) in UnitMaxHp.Where(kv => kv.Value <= 0 || !Enum.IsDefined(kv.Key)))
            {
                errors.Add($"HP máximo inválido para {type} ({hp}).");
            }
        }

        ValidateStats(errors, Soldier, nameof(Soldier));
        ValidateStats(errors, Tower, nameof(Tower));
        return errors;
    }

    /// <summary>Valida la configuración completa.</summary>
    /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
    public void Validate()
    {
        IReadOnlyList<string> errors = GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new ArgumentException("Configuración de Combat inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    private static void ValidateStats(List<string> errors, AttackStats? stats, string name)
    {
        if (stats is null)
        {
            errors.Add($"{name} es obligatorio.");
            return;
        }

        if (stats.Damage <= 0)
        {
            errors.Add($"{name}.Damage debe ser positivo ({stats.Damage}).");
        }

        if (!double.IsFinite(stats.Range) || stats.Range <= 0)
        {
            errors.Add($"{name}.Range debe ser finito y positivo ({stats.Range}).");
        }

        if (stats.CooldownTicks <= 0)
        {
            errors.Add($"{name}.CooldownTicks debe ser positivo ({stats.CooldownTicks}).");
        }
    }
}
