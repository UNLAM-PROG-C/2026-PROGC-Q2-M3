using Rts.Integration.Tests.Support;
using Rts.Server.Configuration;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.World;
using Rts.Simulation.Map;
using Rts.Simulation.State;

namespace Rts.Integration.Tests;

/// <summary>
/// Production <b>real</b> dentro del motor de Simulation (work-plan §4, AGENTS §7): un <c>TrainUnit</c> como lo mandaría un cliente
/// termina en una unidad nueva, cobrada una sola vez, y dos apariciones del mismo commit nunca comparten celda. Las reglas finas
/// de la fase 8 las cubren las pruebas del módulo; acá se verifica que las piezas encajen.
/// </summary>
public class ProductionTests
{
  /// <summary>Margen sobre la duración de entrenamiento: el comando se procesa recién en el tick siguiente al envío.</summary>
  private const int TicksSafetyMargin = 10;

  private const int TicksWaitingWithoutFood = 50;
  private const int OnlyRingOne = 1;
  private const int NextTick = 1;

  // Mapa de la disputa: los dos Centros Principales (4×4) separados por una sola columna, la 6. Los trabajadores iniciales tapan
  // todo el anillo 1 de cada centro salvo esa columna, así que las únicas celdas libres para aparecer son las que comparten.
  private const double CrowdedMapCellSize = 1.0;
  private const int CrowdedMapCells = 16;
  private const int SharedColumn = 6;
  private static readonly GridCell P1CenterAnchor = new(2, 6);
  private static readonly GridCell P2CenterAnchor = new(7, 6);
  private static readonly GridCell BestSharedCell = new(SharedColumn, 7);
  private static readonly GridCell SecondBestSharedCell = new(SharedColumn, 8);

  [Fact]
  public void ATrainedWorker_AppearsNextToItsMainCenter_ExactlyAfterItsTrainingTime_AndIsChargedOnce()
  {
    ServerConfig config = IntegrationMatch.LoadConfig();
    var match = IntegrationMatch.Start(config, IntegrationMatch.LoadConfiguredMap(config), runDomainPhaseInParallel: true);
    BuildingEntity center = match.MainCenterOf(PlayerId.One);
    int initialFood = FoodOf(match, PlayerId.One);
    int duration = config.Production.TrainingDurationTicks[UnitType.Worker];

    IdempotencyKey train = match.Submit(PlayerId.One, new TrainUnitCommand(center.Id, UnitType.Worker));
    match.AdvanceUntil(_ => Spawns(match).Count > 0, duration + TicksSafetyMargin);

    CommandResult result = match.ResultOf(train);
    (TickId tick, UnitSpawned spawned) = Assert.Single(Spawns(match));
    GridCell cell = match.World.Grid.ToCell(spawned.Position);
    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.Equal(result.Tick.Value + duration, tick.Value); // Iniciada en N con duración D, aparece en N+D (D-037).
    Assert.Equal(match.World.Grid.CellCenter(cell), spawned.Position);
    Assert.InRange(RingOf(cell, center.Footprint), OnlyRingOne, config.Production.SpawnSearchRadiusCells);
    Assert.Equal(initialFood - config.Economy.UnitFoodCosts[UnitType.Worker], FoodOf(match, PlayerId.One));
    Assert.Equal(UnitOrderKind.Idle, match.World.Units[spawned.UnitId].Order.Kind);
  }

  [Fact]
  public void TwoWorkersQueuedInOneCenter_AppearOneAfterTheOther_OnDifferentCells()
  {
    ServerConfig config = IntegrationMatch.LoadConfig();
    var match = IntegrationMatch.Start(config, IntegrationMatch.LoadConfiguredMap(config), runDomainPhaseInParallel: true);
    BuildingEntity center = match.MainCenterOf(PlayerId.One);
    int duration = config.Production.TrainingDurationTicks[UnitType.Worker];

    match.Submit(PlayerId.One, new TrainUnitCommand(center.Id, UnitType.Worker));
    match.Submit(PlayerId.One, new TrainUnitCommand(center.Id, UnitType.Worker));
    match.AdvanceUntil(_ => Spawns(match).Count > 1, (duration * 2) + TicksSafetyMargin);

    (TickId Tick, UnitSpawned Unit)[] spawns = [.. Spawns(match)];
    // La segunda tarea empieza recién el tick siguiente a la primera aparición (05 §4), y la primera unidad sigue en su celda.
    Assert.Equal(spawns[0].Tick.Value + NextTick + duration, spawns[1].Tick.Value);
    Assert.NotEqual(spawns[0].Unit.Position, spawns[1].Unit.Position);
    Assert.NotEqual(spawns[0].Unit.UnitId, spawns[1].Unit.UnitId);
  }

  [Fact]
  public void ARequestThePlayerCannotAfford_IsAccepted_ButWaitsWithoutBeingCharged()
  {
    ServerConfig example = IntegrationMatch.LoadConfig();
    int tooExpensive = example.Economy.InitialBalance.Food + 1;
    ServerConfig config = WithWorkerCost(example, tooExpensive);
    var match = IntegrationMatch.Start(config, IntegrationMatch.LoadConfiguredMap(config), runDomainPhaseInParallel: true);
    BuildingEntity center = match.MainCenterOf(PlayerId.One);

    IdempotencyKey train = match.Submit(PlayerId.One, new TrainUnitCommand(center.Id, UnitType.Worker));
    for (int tick = 0; tick < TicksWaitingWithoutFood; tick++)
    {
      match.Advance();
    }

    // Pedir no exige comida: se cobra al iniciar (05 §2). Sin comida, la solicitud queda en la cola y nada se descuenta.
    Assert.Equal(CommandOutcome.Accepted, match.ResultOf(train).Outcome);
    Assert.Equal(ProducerStatus.WaitingForResources, match.MainCenterOf(PlayerId.One).Production!.Status);
    Assert.Equal(example.Economy.InitialBalance.Food, FoodOf(match, PlayerId.One));
    Assert.Empty(Spawns(match));
  }

  [Fact]
  public void TwoSpawnsInTheSameCommit_NeverPickTheSameFreeCell_SameInParallelAndSequential()
  {
    ServerConfig config = OnlySearchingRingOne(IntegrationMatch.LoadConfig());

    IntegrationMatch parallel = RunDisputedSpawns(config, runDomainPhaseInParallel: true);
    IntegrationMatch sequential = RunDisputedSpawns(config, runDomainPhaseInParallel: false);

    // Los dos centros prefieren la misma celda libre, la (6,7). Se la queda la solicitud más antigua (P1, por Sequence) y la
    // otra aparición, del mismo tick, toma la siguiente en vez de repetirla (05 §4, D-041).
    (TickId Tick, UnitSpawned Unit)[] spawns = [.. Spawns(parallel)];
    Assert.Equal(2, spawns.Length);
    Assert.Equal(spawns[0].Tick, spawns[1].Tick);
    Assert.Equal(parallel.World.Grid.CellCenter(BestSharedCell), SpawnOf(spawns, PlayerId.One).Position);
    Assert.Equal(parallel.World.Grid.CellCenter(SecondBestSharedCell), SpawnOf(spawns, PlayerId.Two).Position);
    Assert.Equal(sequential.Publications, parallel.Publications);
    Assert.Equal(WorldViewProjector.Project(sequential.World), WorldViewProjector.Project(parallel.World));
  }

  private static IntegrationMatch RunDisputedSpawns(ServerConfig config, bool runDomainPhaseInParallel)
  {
    var match = IntegrationMatch.Start(config, CrowdedMap(IntegrationMatch.MainCenterSize(config)), runDomainPhaseInParallel);
    int duration = config.Production.TrainingDurationTicks[UnitType.Worker];

    match.Submit(PlayerId.One, new TrainUnitCommand(match.MainCenterOf(PlayerId.One).Id, UnitType.Worker));
    match.Submit(PlayerId.Two, new TrainUnitCommand(match.MainCenterOf(PlayerId.Two).Id, UnitType.Worker));
    match.AdvanceUntil(_ => Spawns(match).Count > 1, duration + TicksSafetyMargin);
    return match;
  }

  private static MapDefinition CrowdedMap(FootprintSize centerSize) => new(
      new GridSpec(CrowdedMapCellSize, CrowdedMapCells, CrowdedMapCells),
      [],
      [
        new PlayerStartDefinition(PlayerId.One, P1CenterAnchor, WorkersBlockingRingOne(new Footprint(P1CenterAnchor, centerSize))),
        new PlayerStartDefinition(PlayerId.Two, P2CenterAnchor, WorkersBlockingRingOne(new Footprint(P2CenterAnchor, centerSize))),
      ]);

  /// <summary>Un trabajador en cada celda del anillo 1 de la huella, menos las de la columna compartida.</summary>
  private static EquatableArray<InitialUnitDefinition> WorkersBlockingRingOne(Footprint center) =>
  [
    .. Enumerable.Range(center.MinRow - OnlyRingOne, center.Size.Height + (2 * OnlyRingOne))
        .SelectMany(row => Enumerable.Range(center.MinColumn - OnlyRingOne, center.Size.Width + (2 * OnlyRingOne)).Select(column => new GridCell(column, row)))
        .Where(cell => RingOf(cell, center) == OnlyRingOne && cell.Column != SharedColumn)
        .Select(cell => new InitialUnitDefinition(UnitType.Worker, new PositionXZ((cell.Column + 0.5) * CrowdedMapCellSize, (cell.Row + 0.5) * CrowdedMapCellSize))),
  ];

  private static ServerConfig OnlySearchingRingOne(ServerConfig config) =>
      config with { Production = config.Production with { SpawnSearchRadiusCells = OnlyRingOne } };

  private static ServerConfig WithWorkerCost(ServerConfig config, int workerCost)
  {
    var costs = new Dictionary<UnitType, int>(config.Economy.UnitFoodCosts) { [UnitType.Worker] = workerCost };
    return config with { Economy = config.Economy with { UnitFoodCosts = costs } };
  }

  private static int FoodOf(IntegrationMatch match, PlayerId player) => match.World.Players[player].Resources.Food;

  /// <summary>Las apariciones publicadas hasta ahora, con su tick, en el orden en que ocurrieron.</summary>
  private static List<(TickId Tick, UnitSpawned Unit)> Spawns(IntegrationMatch match) =>
  [
    .. match.Publications.SelectMany(publication =>
        publication.Events.Select(e => e.Payload).OfType<UnitSpawned>().Select(spawned => (publication.Tick, spawned))),
  ];

  private static UnitSpawned SpawnOf((TickId Tick, UnitSpawned Unit)[] spawns, PlayerId owner) =>
      spawns.Single(spawn => spawn.Unit.Owner == owner).Unit;

  /// <summary>Distancia de Chebyshev de una celda al rectángulo de una huella: el número de anillo (0 si está adentro).</summary>
  private static int RingOf(GridCell cell, Footprint footprint)
  {
    int columns = Math.Max(Math.Max(footprint.MinColumn - cell.Column, 0), cell.Column - (footprint.EndColumn - 1));
    int rows = Math.Max(Math.Max(footprint.MinRow - cell.Row, 0), cell.Row - (footprint.EndRow - 1));
    return Math.Max(columns, rows);
  }
}
