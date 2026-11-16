using Rts.Networking.Parsing;
using Rts.Protocol.Commands;
using Rts.Protocol.State;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests.Parsing;

// El lector convierte DTOs ya deserializados en comandos de dominio. Los casos de punta a punta (JSON → comando) siguen en
// InboundMessageParserTests; acá se prueba la conversión sola, sin JSON.
public class GameplayCommandReaderTests
{
  private const int UnitId = 3;
  private const int BuildingId = 20;
  private const int AnchorColumn = 4;
  private const int AnchorRow = 5;
  private const long RouteMapRevision = 2;

  private static readonly IReadOnlyList<UnitRouteDto> OneRoute = [new UnitRouteDto(UnitId, [new PositionDto(9, 5)])];

  [Fact]
  public void ToCommand_BuildWithPlacementFields_ReturnsPlacement()
  {
    var dto = new BuildCommandDto(BuildingType.Farm, AnchorColumn, AnchorRow, null, RouteMapRevision, OneRoute);

    BuildCommand command = GameplayCommandReader.ToCommand(dto);

    Assert.True(command.IsPlacement);
    Assert.Equal(new GridCell(AnchorColumn, AnchorRow), command.Anchor);
  }

  [Fact]
  public void ToCommand_BuildWithOnlyBuildingId_ReturnsContinuation()
  {
    var dto = new BuildCommandDto(null, null, null, BuildingId, RouteMapRevision, OneRoute);

    BuildCommand command = GameplayCommandReader.ToCommand(dto);

    Assert.Equal(new EntityId(BuildingId), command.ExistingBuildingId);
  }

  [Fact]
  public void ToCommand_BuildMixingBothForms_Throws()
  {
    var dto = new BuildCommandDto(BuildingType.Farm, AnchorColumn, AnchorRow, BuildingId, RouteMapRevision, OneRoute);

    Assert.Throws<ArgumentException>(() => GameplayCommandReader.ToCommand(dto));
  }

  [Fact]
  public void ToCommand_StopWithoutUnitIds_Throws()
  {
    var dto = new StopCommandDto(null!);

    Assert.Throws<ArgumentException>(() => GameplayCommandReader.ToCommand(dto));
  }

  [Fact]
  public void ToCommand_RouteWithNullWaypoint_Throws()
  {
    var dto = new AttackCommandDto(BuildingId, RouteMapRevision, [new UnitRouteDto(UnitId, [null!])]);

    Assert.Throws<ArgumentException>(() => GameplayCommandReader.ToCommand(dto));
  }

  [Fact]
  public void ToCommand_TrainUnit_KeepsProducerAndType()
  {
    var dto = new TrainUnitCommandDto(BuildingId, UnitType.Worker);

    TrainUnitCommand command = GameplayCommandReader.ToCommand(dto);

    Assert.Equal(new EntityId(BuildingId), command.ProducerId);
    Assert.Equal(UnitType.Worker, command.UnitType);
  }
}
