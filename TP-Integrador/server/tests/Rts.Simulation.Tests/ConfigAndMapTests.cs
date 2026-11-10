using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Time;
using Rts.Simulation.Configuration;
using Rts.Simulation.Map;

namespace Rts.Simulation.Tests;

public class ConfigAndMapTests
{
  private static readonly FootprintSize CenterSize = new(4, 4);

  /// <summary>Configuración válida SOLO para pruebas; los números son suposiciones temporales.</summary>
  internal static SimulationConfig ValidConfig() => new()
  {
    MaxCommandsPerTick = 30,
    CommandQueueCapacity = 1000,
    BarrierTimeout = TimeSpan.FromSeconds(2),
  };

  private static MapDefinition SymmetricMap() => new(
      new GridSpec(1.0, 40, 40),
      [new GridCell(10, 20), new GridCell(29, 19)],
      [
          new PlayerStartDefinition(PlayerId.One, new GridCell(2, 2),
                [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(7.5, 3.5))]),
            new PlayerStartDefinition(PlayerId.Two, new GridCell(34, 34),
                [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(32.5, 36.5))]),
      ]);

  [Fact]
  public void Config_DefaultsToApprovedTickRate()
  {
    SimulationConfig config = ValidConfig();

    Assert.Equal(TickRate.Default, config.TickRate);
    Assert.True(config.RunDomainPhaseInParallel);
    Assert.Empty(config.GetValidationErrors());
  }

  [Fact]
  public void Config_RejectsInvalidLimitsAndTimeouts()
  {
    SimulationConfig config = ValidConfig() with
    {
      MaxCommandsPerTick = 0,
      CommandQueueCapacity = -1,
      BarrierTimeout = TimeSpan.FromMilliseconds(10),
    };

    Assert.Equal(3, config.GetValidationErrors().Count);
    Assert.Throws<ArgumentException>(config.Validate);
  }

  [Fact]
  public void Map_SymmetricExampleIsValid()
  {
    Assert.Empty(SymmetricMap().GetValidationErrors(CenterSize));
  }

  [Fact]
  public void Map_RequiresOneStartPerPlayer()
  {
    MapDefinition map = SymmetricMap() with { Starts = [SymmetricMap().Starts[0]] };

    Assert.Contains(map.GetValidationErrors(CenterSize), e => e.Contains("P1") && e.Contains("P2"));
  }

  [Fact]
  public void Map_DetectsOverlapsAndOutOfBounds()
  {
    MapDefinition map = SymmetricMap() with
    {
      TreeCells = [new GridCell(3, 3), new GridCell(3, 3), new GridCell(45, 1)],
      Starts =
        [
            new PlayerStartDefinition(PlayerId.One, new GridCell(2, 2), [new InitialUnitDefinition(UnitType.Worker, new PositionXZ(3.5, 3.5))]),
                new PlayerStartDefinition(PlayerId.Two, new GridCell(38, 38), default),
            ],
    };

    IReadOnlyList<string> errors = map.GetValidationErrors(CenterSize);

    Assert.Contains(errors, e => e.Contains("duplicado"));
    Assert.Contains(errors, e => e.Contains("Árbol fuera"));
    Assert.Contains(errors, e => e.Contains("se superpone con un árbol"));
    Assert.Contains(errors, e => e.Contains("sale del mapa"));
    Assert.Contains(errors, e => e.Contains("dentro de un Centro Principal"));
  }
}
