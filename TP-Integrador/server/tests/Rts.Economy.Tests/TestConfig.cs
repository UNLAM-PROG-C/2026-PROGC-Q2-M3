using Rts.Economy.Configuration;
using Rts.Shared.Entities;
using Rts.Shared.Resources;

namespace Rts.Economy.Tests;

/// <summary>
/// Configuración válida SOLO para pruebas aisladas. Los números son suposiciones temporales (AGENTS §2.4),
/// no balance aprobado; el balance real sigue PENDIENTE.
/// </summary>
internal static class TestConfig
{
    public static EconomyConfig Valid() => new()
    {
        InitialBalance = new ResourceAmount(Wood: 100, Food: 50),
        MaxWoodPerTree = 12,
        WoodPerGather = 5,
        GatherIntervalTicks = 20,
        TreeRespawnTicks = 600,
        FarmProductionIntervalTicks = 100,
        BuildingWoodCosts = new Dictionary<BuildingType, int>
        {
            [BuildingType.Farm] = 30,
            [BuildingType.Barracks] = 60,
            [BuildingType.Tower] = 50,
            [BuildingType.Fence] = 5,
        },
        FarmRepairWoodCost = 15,
        UnitFoodCosts = new Dictionary<UnitType, int>
        {
            [UnitType.Worker] = 3,
            [UnitType.Soldier] = 5,
        },
    };
}
