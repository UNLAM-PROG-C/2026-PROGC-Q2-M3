using Rts.Combat.Configuration;
using Rts.Server.Configuration;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Resources;
using Rts.Shared.World;

namespace Rts.Server.Composition;

/// <summary>
/// Arma el catálogo público del InitialState desde las configuraciones ya validadas (DECIDIDO D-068, RECOMENDADO D-071). Solo copia
/// valores: cada número tiene una única fuente, la configuración de su módulo dueño.
/// <list type="bullet">
/// <item>HP máximo de unidades: Combat. HP máximo y huella de edificios: Construction.</item>
/// <item>Costos: Economy (madera para edificios y reparación, comida para unidades, D-015).</item>
/// <item>Duración de entrenamiento: Production. Velocidad y rangos de interacción: Movement.</item>
/// <item>Ataques: Combat. Solo atacan el soldado y la torre (02 §2).</item>
/// </list>
/// </summary>
public static class GameCatalogBuilder
{
    /// <exception cref="ArgumentException">Si la configuración no fue validada antes y le falta algún tipo.</exception>
    public static GameCatalog Build(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        EquatableArray<UnitCatalogEntry> units = Enum.GetValues<UnitType>()
            .Select(type => new UnitCatalogEntry(
                type,
                config.Combat.UnitMaxHp[type],
                ResourceAmount.OfFood(config.Economy.UnitFoodCosts[type]),
                config.Production.TrainingDurationTicks[type],
                config.Movement.SpeedPerSecond[type],
                type == UnitType.Soldier ? ToView(config.Combat.Soldier) : null))
            .ToEquatableArray();

        EquatableArray<BuildingCatalogEntry> buildings = Enum.GetValues<BuildingType>()
            .Select(type => new BuildingCatalogEntry(
                type,
                config.Construction.BuildingMaxHp[type],
                config.Construction.FootprintSizes[type],
                config.Economy.BuildingWoodCosts.TryGetValue(type, out int wood) ? ResourceAmount.OfWood(wood) : null,
                type == BuildingType.Tower ? ToView(config.Combat.Tower) : null))
            .ToEquatableArray();

        return new GameCatalog(
            config.Simulation.TickRate,
            units,
            buildings,
            ResourceAmount.OfWood(config.Economy.FarmRepairWoodCost),
            config.Construction.RepairWorkRequired,
            config.Movement.GatherRange,
            config.Movement.BuildRange);
    }

    private static AttackStatsView ToView(AttackStats stats) => new(stats.Damage, stats.Range, stats.CooldownTicks);
}
