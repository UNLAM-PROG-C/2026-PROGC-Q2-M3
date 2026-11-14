using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using static Rts.Production.Tests.Fixtures;

namespace Rts.Production.Tests;

/// <summary>Validación de <c>TrainUnit</c> en la fase 1 y consultas de duración (contrato 05 §3).</summary>
public class ProductionRulesTests
{
  private const int Pending = 1;

  private static readonly ProductionRules Rules = Fixtures.Rules();

  private static ProducerInfo Info(
      PlayerId owner = default, BuildingType type = BuildingType.MainCenter, bool operational = true, int pending = 0) =>
      new(CenterId(P1), owner == default ? P1 : owner, type, operational, pending);

  [Fact]
  public void ACompletedProducerWithRoom_AdmitsTheRequest()
  {
    Assert.Null(Rules.EvaluateTrainRequest(P1, UnitType.Worker, Info()));
    Assert.Null(Rules.EvaluateTrainRequest(P1, UnitType.Soldier, Info(type: BuildingType.Barracks)));
  }

  [Fact]
  public void NoFoodIsRequiredToRequest_BecauseItIsChargedWhenTheTrainingStarts()
  {
    // EvaluateTrainRequest ni siquiera recibe la comida: el pedido se admite aunque el jugador no pueda pagarlo todavía.
    Assert.Null(Rules.EvaluateTrainRequest(P2, UnitType.Worker, Info(owner: P2)));
  }

  [Fact]
  public void AMissingProducer_IsNotFound()
  {
    Assert.Equal(CommonErrors.EntityNotFound, Rules.EvaluateTrainRequest(P1, UnitType.Worker, null));
  }

  [Fact]
  public void AnotherPlayersProducer_IsNotOwned()
  {
    Assert.Equal(CommonErrors.NotOwner, Rules.EvaluateTrainRequest(P2, UnitType.Worker, Info(owner: P1)));
  }

  [Theory]
  [InlineData(BuildingType.Farm)]
  [InlineData(BuildingType.Tower)]
  [InlineData(BuildingType.Fence)]
  public void ABuildingThatDoesNotProduce_IsRejected(BuildingType type)
  {
    Assert.Equal(ProductionErrors.NotAProducer, Rules.EvaluateTrainRequest(P1, UnitType.Worker, Info(type: type)));
  }

  [Theory]
  [InlineData(BuildingType.MainCenter, UnitType.Soldier)]
  [InlineData(BuildingType.Barracks, UnitType.Worker)]
  public void AProducerOnlyTrainsItsOwnUnit(BuildingType type, UnitType wrongUnit)
  {
    Assert.Equal(ProductionErrors.WrongUnitType, Rules.EvaluateTrainRequest(P1, wrongUnit, Info(type: type)));
  }

  [Fact]
  public void AProducerThatIsNotOperationalYet_IsRejected()
  {
    Assert.Equal(ProductionErrors.ProducerNotOperational, Rules.EvaluateTrainRequest(P1, UnitType.Worker, Info(operational: false)));
  }

  [Fact]
  public void TheQueueAdmitsUpToTheLimit_AndRejectsOneMore()
  {
    int limit = Config().MaxPendingPerProducer;

    Assert.Null(Rules.EvaluateTrainRequest(P1, UnitType.Worker, Info(pending: limit - Pending)));
    Assert.Equal(ProductionErrors.QueueFull, Rules.EvaluateTrainRequest(P1, UnitType.Worker, Info(pending: limit)));
  }

  [Fact]
  public void TheChecksFollowTheContractOrder_TheFirstFailureIsTheOneReported()
  {
    int full = Config().MaxPendingPerProducer;

    // Cada pedido falla en varias cosas a la vez; se informa la primera de la tabla del contrato.
    Assert.Equal(CommonErrors.NotOwner, Rules.EvaluateTrainRequest(P2, UnitType.Soldier, Info(owner: P1, type: BuildingType.Farm, operational: false, pending: full)));
    Assert.Equal(ProductionErrors.NotAProducer, Rules.EvaluateTrainRequest(P1, UnitType.Soldier, Info(type: BuildingType.Farm, operational: false, pending: full)));
    Assert.Equal(ProductionErrors.WrongUnitType, Rules.EvaluateTrainRequest(P1, UnitType.Soldier, Info(operational: false, pending: full)));
    Assert.Equal(ProductionErrors.ProducerNotOperational, Rules.EvaluateTrainRequest(P1, UnitType.Worker, Info(operational: false, pending: full)));
  }

  [Fact]
  public void TrainingDuration_ComesFromTheConfiguration()
  {
    Assert.Equal(WorkerDuration, Rules.GetTrainingDurationTicks(UnitType.Worker));
    Assert.Equal(SoldierDuration, Rules.GetTrainingDurationTicks(UnitType.Soldier));
  }

  [Fact]
  public void AnUnknownUnitType_HasNoDuration()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => Rules.GetTrainingDurationTicks((UnitType)int.MaxValue));
  }

  [Fact]
  public void Constructor_RejectsAnInvalidConfigurationAndNull()
  {
    Assert.Throws<ArgumentNullException>(() => new ProductionRules(null!));
    Assert.Throws<ArgumentException>(() => new ProductionRules(Config() with { MaxPendingPerProducer = 0 }));
  }
}
