using Rts.Server.Configuration;

namespace Rts.Server.Validation;

/// <summary>
/// Validación completa al arrancar (contrato 10 §3): primero la de cada módulo y después las <b>cruzadas</b>, que ningún módulo puede
/// hacer solo porque mezclan configuraciones de varios. Con cualquier error, el servidor no arranca. Función pura: no lee archivos.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>Devuelve todos los problemas encontrados, con el prefijo de su sección; vacía si la configuración es válida.</summary>
    public static IReadOnlyList<string> Validate(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>();

        Add("simulation", config.Simulation.GetValidationErrors());
        Add("economy", config.Economy.GetValidationErrors());
        Add("combat", config.Combat.GetValidationErrors());
        Add("movement", config.Movement.GetValidationErrors());
        Add("construction", config.Construction.GetValidationErrors());
        Add("production", config.Production.GetValidationErrors());
        Add("gameSession", config.GameSession.GetValidationErrors());
        Add("networking", config.Networking.GetValidationErrors());

        // Cruzada D-062: la cadencia de posiciones debe dividir exacto el tick rate de Simulation.
        Add("stateSync", config.StateSync.GetValidationErrors(config.Simulation.TickRate));

        // Cruzada D-030: el soldado se detiene a la distancia desde la que Combat le permite atacar.
        if (config.Movement.AttackRange != config.Combat.Soldier.Range)
        {
            errors.Add($"[movement/combat] movement.attackRange ({config.Movement.AttackRange}) debe ser igual a combat.soldier.range ({config.Combat.Soldier.Range}) (D-030).");
        }

        if (!File.Exists(config.MapFile))
        {
            errors.Add($"[mapFile] No existe el archivo de mapa '{config.MapFile}'.");
        }

        return errors;

        void Add(string section, IEnumerable<string> sectionErrors) => errors.AddRange(sectionErrors.Select(e => $"[{section}] {e}"));
    }
}
