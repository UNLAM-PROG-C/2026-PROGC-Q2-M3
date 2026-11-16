using Rts.Production;
using Rts.Production.Configuration;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Server.Composition;

/// <summary>
/// <b>PROVISORIO</b> (ver «Comportamientos provisorios» en <c>docs/decisions.md</c>): ocupa el lugar de Production mientras el
/// módulo real no exista, para que el servidor pueda armarse y jugarse. Se borra cuando llegue (paso 30 del plan de Server).
/// <para>
/// Nunca lanza: una excepción en el tick sería una falla crítica y terminaría la partida. En cambio, todo <c>TrainUnit</c> se
/// rechaza con <see cref="NotAvailable"/> (el cliente recibe <c>CommandRejected</c>) y la fase 8 no tiene nada que avanzar.
/// </para>
/// </summary>
internal sealed class UnavailableProduction(ProductionConfig config) : IProductionRules, IProductionCalculator
{
  /// <summary>Código provisorio (área Production, D-007): el módulo de producción todavía no está implementado.</summary>
  public static readonly ErrorCode NotAvailable = new("Production.NotAvailable");

  /// <summary>Dato de configuración, no de lógica: se puede responder con la verdad.</summary>
  public int GetTrainingDurationTicks(UnitType unitType) => config.TrainingDurationTicks[unitType];

  public ErrorCode? EvaluateTrainRequest(PlayerId requester, UnitType unitType, ProducerInfo? producer) => NotAvailable;

  /// <summary>Sin entrenamientos no hay apariciones; "no hay celda" es una respuesta válida del contrato.</summary>
  public GridCell? FindSpawnCell(Footprint producerFootprint, SpawnView view, IReadOnlySet<GridCell> alreadyReserved) => null;

  public ProductionTickResult CalculateTick(ProductionTickInput input) => ProductionTickResult.Empty(input.Tick);
}
