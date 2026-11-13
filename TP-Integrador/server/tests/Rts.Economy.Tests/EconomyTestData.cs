using Rts.Economy.Configuration;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Ids;

namespace Rts.Economy.Tests;

/// <summary>
/// Datos de prueba de la implementación de Economy. Los valores esperados se derivan de <see cref="TestConfig"/> para
/// que las pruebas no dependan de números sueltos: si cambia la configuración de prueba, las pruebas siguen valiendo.
/// </summary>
internal static class EconomyTestData
{
  public const int OneTick = 1;

  public static readonly EconomyConfig Config = TestConfig.Valid();
  public static readonly TickId Now = new(1_000);

  public static readonly EntityId Worker1 = new(1);
  public static readonly EntityId Worker2 = new(2);
  public static readonly EntityId Worker3 = new(3);
  public static readonly EntityId Worker4 = new(4);
  public static readonly EntityId TreeA = new(100);
  public static readonly EntityId TreeB = new(101);
  public static readonly EntityId FarmA = new(200);
  public static readonly EntityId FarmB = new(201);

  /// <summary>Contador de un recolector que completa su intervalo en el próximo tick.</summary>
  public static int OneTickBeforeGather => Config.GatherIntervalTicks - OneTick;

  /// <summary>Temporizador de una granja que produce en el próximo tick.</summary>
  public static int OneTickBeforeBatch => Config.FarmProductionIntervalTicks - OneTick;

  public static GathererState Gatherer(EntityId worker, EntityId tree, int ticksAccumulated, PlayerId? owner = null) =>
      new(worker, owner ?? PlayerId.One, tree, ticksAccumulated);

  public static GathererState ReadyGatherer(EntityId worker, EntityId tree, PlayerId? owner = null) =>
      Gatherer(worker, tree, OneTickBeforeGather, owner);

  public static FarmEconomyState Farm(EntityId farm, PlayerId owner, int batches, int ticksAccumulated, TickId activeFrom) =>
      new(farm, owner, FarmProductionStatus.Producing, batches, ticksAccumulated, activeFrom);

  /// <summary>Granja recién completada o reparada: ciclo completo, temporizador en 0.</summary>
  public static FarmEconomyState FreshFarm(EntityId farm, PlayerId owner, TickId activeFrom) =>
      Farm(farm, owner, Config.BatchesPerFarmCycle, ticksAccumulated: 0, activeFrom);

  public static EconomyTickInput Input(
      TickId tick,
      EquatableArray<GathererState> gatherers = default,
      EquatableArray<TreeEconomyState> trees = default,
      EquatableArray<FarmEconomyState> farms = default) => new(tick, gatherers, trees, farms);

  public static EconomyTickResult Calculate(EconomyTickInput input) => new EconomyCalculator(Config).CalculateTick(input);
}
