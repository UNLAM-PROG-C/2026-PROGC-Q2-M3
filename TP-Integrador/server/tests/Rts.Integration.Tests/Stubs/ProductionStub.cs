using Rts.Production;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Integration.Tests.Stubs;

/// <summary>
/// Reemplazo provisorio de Production, que todavía no está implementado (es de Joaco). El tick no tiene colas que avanzar; las
/// reglas lanzan, así que un <c>TrainUnit</c> en un escenario hace fallar la prueba a la vista. Se borra cuando exista el módulo real.
/// </summary>
internal sealed class ProductionStub : IProductionRules, IProductionCalculator
{
  public ProductionTickResult CalculateTick(ProductionTickInput input) => ProductionTickResult.Empty(input.Tick);

  public int GetTrainingDurationTicks(UnitType unitType) => throw NotImplementedYet();

  public ErrorCode? EvaluateTrainRequest(PlayerId requester, UnitType unitType, ProducerInfo? producer) => throw NotImplementedYet();

  public GridCell? FindSpawnCell(Footprint producerFootprint, SpawnView view, IReadOnlySet<GridCell> alreadyReserved) =>
      throw NotImplementedYet();

  private static NotSupportedException NotImplementedYet() =>
      new("Production todavía no está implementado: ningún escenario de integración debería llegar acá.");
}
