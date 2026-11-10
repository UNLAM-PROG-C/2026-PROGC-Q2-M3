using Rts.Construction.Configuration;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;

namespace Rts.Construction.Tests;

public class ConstructionConfigTests
{
    /// <summary>Configuración válida SOLO para pruebas; los números son suposiciones temporales, no balance aprobado.</summary>
    internal static ConstructionConfig Valid() => new()
    {
        BuildingMaxHp = new Dictionary<BuildingType, int>
        {
            [BuildingType.MainCenter] = 1000,
            [BuildingType.Farm] = 200,
            [BuildingType.Barracks] = 400,
            [BuildingType.Tower] = 300,
            [BuildingType.Fence] = 80,
        },
        FootprintSizes = new Dictionary<BuildingType, FootprintSize>
        {
            [BuildingType.MainCenter] = new(4, 4),
            [BuildingType.Farm] = new(3, 3),
            [BuildingType.Barracks] = new(3, 3),
            [BuildingType.Tower] = new(2, 2),
            [BuildingType.Fence] = new(1, 1),
        },
        InitialHpFraction = 0.1,
        BuildHpPerWorkerTick = 2,
        RepairWorkRequired = 300,
        RepairWorkPerWorkerTick = 1,
    };

    [Fact]
    public void ValidConfig_HasNoErrors()
    {
        Assert.Empty(Valid().GetValidationErrors());
        Valid().Validate();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void InitialHpFraction_MustBeInOpenUnitInterval(double fraction)
    {
        Assert.Single((Valid() with { InitialHpFraction = fraction }).GetValidationErrors());
    }

    [Fact]
    public void Fence_MustBeSingleCell()
    {
        ConstructionConfig config = Valid() with
        {
            FootprintSizes = new Dictionary<BuildingType, FootprintSize>(Valid().FootprintSizes)
            {
                [BuildingType.Fence] = new(2, 1),
            },
        };

        Assert.Contains(config.GetValidationErrors(), e => e.Contains("valla", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AllBuildingTypes_NeedMaxHpAndFootprint()
    {
        ConstructionConfig config = Valid() with
        {
            BuildingMaxHp = new Dictionary<BuildingType, int> { [BuildingType.Farm] = 200 },
            FootprintSizes = new Dictionary<BuildingType, FootprintSize> { [BuildingType.Farm] = new(3, 3) },
        };

        Assert.Equal(8, config.GetValidationErrors().Count);
    }

    [Fact]
    public void WorkValues_MustBePositive()
    {
        ConstructionConfig config = Valid() with { BuildHpPerWorkerTick = 0, RepairWorkRequired = -5, RepairWorkPerWorkerTick = 0 };

        Assert.Equal(3, config.GetValidationErrors().Count);
        Assert.Throws<ArgumentException>(config.Validate);
    }
}
