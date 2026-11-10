using Rts.Shared.Entities;
using Rts.Shared.Resources;

namespace Rts.Economy.Configuration;

/// <summary>
/// Parámetros económicos tipados (01 §10). Fuente única de costos del juego (D-015).
/// Los valores numéricos están PENDIENTES de balance, salvo <see cref="FoodPerFarmBatch"/> = 7 y
/// <see cref="BatchesPerFarmCycle"/> = 5, que vienen de la definición funcional y requieren una nueva decisión para cambiarse.
/// Todos los intervalos se expresan en ticks. Se valida una vez al iniciar el servidor con <see cref="Validate"/>.
/// </summary>
public sealed record EconomyConfig
{
    /// <summary>Tipos de edificio que se colocan durante la partida y por lo tanto requieren costo.</summary>
    public static readonly IReadOnlyList<BuildingType> BuildableTypes =
        [BuildingType.Farm, BuildingType.Barracks, BuildingType.Tower, BuildingType.Fence];

    /// <summary>Saldo inicial de cada jugador (igual para ambos).</summary>
    public required ResourceAmount InitialBalance { get; init; }

    /// <summary>Madera de un árbol completo (al inicio y al regenerarse).</summary>
    public required int MaxWoodPerTree { get; init; }

    /// <summary>Madera que solicita un trabajador cada vez que completa su intervalo.</summary>
    public required int WoodPerGather { get; init; }

    /// <summary>Ticks en rango que necesita un trabajador para completar una recolección.</summary>
    public required int GatherIntervalTicks { get; init; }

    /// <summary>Ticks desde el agotamiento hasta la regeneración del árbol.</summary>
    public required int TreeRespawnTicks { get; init; }

    /// <summary>Ticks productivos entre tandas de una granja.</summary>
    public required int FarmProductionIntervalTicks { get; init; }

    /// <summary>Comida por tanda. Valor de la definición funcional: 7.</summary>
    public int FoodPerFarmBatch { get; init; } = 7;

    /// <summary>Tandas por ciclo antes de agotarse. Valor de la definición funcional: 5.</summary>
    public int BatchesPerFarmCycle { get; init; } = 5;

    /// <summary>Costo en madera por tipo de edificio construible (exactamente <see cref="BuildableTypes"/>).</summary>
    public required IReadOnlyDictionary<BuildingType, int> BuildingWoodCosts { get; init; }

    /// <summary>Costo en madera de iniciar la reparación de capacidad de una granja.</summary>
    public required int FarmRepairWoodCost { get; init; }

    /// <summary>Costo en comida por tipo de unidad (trabajador y soldado).</summary>
    public required IReadOnlyDictionary<UnitType, int> UnitFoodCosts { get; init; }

    /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (!InitialBalance.IsNonNegative)
        {
            errors.Add($"{nameof(InitialBalance)} no puede tener componentes negativas.");
        }

        RequirePositive(errors, MaxWoodPerTree, nameof(MaxWoodPerTree));
        RequirePositive(errors, WoodPerGather, nameof(WoodPerGather));
        RequirePositive(errors, GatherIntervalTicks, nameof(GatherIntervalTicks));
        RequirePositive(errors, TreeRespawnTicks, nameof(TreeRespawnTicks));
        RequirePositive(errors, FarmProductionIntervalTicks, nameof(FarmProductionIntervalTicks));
        RequirePositive(errors, FoodPerFarmBatch, nameof(FoodPerFarmBatch));
        RequirePositive(errors, BatchesPerFarmCycle, nameof(BatchesPerFarmCycle));

        if (FarmRepairWoodCost < 0)
        {
            errors.Add($"{nameof(FarmRepairWoodCost)} no puede ser negativo.");
        }

        if (BuildingWoodCosts is null)
        {
            errors.Add($"{nameof(BuildingWoodCosts)} es obligatorio.");
        }
        else
        {
            foreach (BuildingType type in BuildableTypes.Where(t => !BuildingWoodCosts.ContainsKey(t)))
            {
                errors.Add($"Falta el costo del edificio {type}.");
            }

            foreach (BuildingType type in BuildingWoodCosts.Keys.Where(t => !BuildableTypes.Contains(t)))
            {
                errors.Add($"{type} no se construye durante la partida y no debe tener costo.");
            }

            foreach ((BuildingType type, int cost) in BuildingWoodCosts.Where(kv => kv.Value < 0))
            {
                errors.Add($"El costo de {type} no puede ser negativo ({cost}).");
            }
        }

        if (UnitFoodCosts is null)
        {
            errors.Add($"{nameof(UnitFoodCosts)} es obligatorio.");
        }
        else
        {
            foreach (UnitType type in Enum.GetValues<UnitType>().Where(t => !UnitFoodCosts.ContainsKey(t)))
            {
                errors.Add($"Falta el costo de entrenamiento de {type}.");
            }

            foreach ((UnitType type, int cost) in UnitFoodCosts.Where(kv => kv.Value < 0 || !Enum.IsDefined(kv.Key)))
            {
                errors.Add($"Costo de entrenamiento inválido para {type} ({cost}).");
            }
        }

        return errors;
    }

    /// <summary>Valida la configuración completa.</summary>
    /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
    public void Validate()
    {
        IReadOnlyList<string> errors = GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new ArgumentException("Configuración de Economy inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
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
