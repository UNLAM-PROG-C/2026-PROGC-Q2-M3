using Rts.Production.Configuration;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Production.Tests;

/// <summary>
/// Datos de prueba para Production. Los números son suposiciones temporales para poder armar escenarios cortos, no balance aprobado.
/// Cada jugador tiene un Centro Principal (entrena trabajadores) y un Cuartel (entrena soldados).
/// </summary>
internal static class Fixtures
{
  public const int WorkerCost = 3;
  public const int SoldierCost = 5;
  public const int WorkerDuration = 5;
  public const int SoldierDuration = 8;
  public const int PlentyOfFood = 100;
  public const int DefaultRadius = 3;
  private const int MaxPending = 3;

  public static readonly PlayerId P1 = PlayerId.One;
  public static readonly PlayerId P2 = PlayerId.Two;
  public static readonly GridSpec Grid = new(1.0, 30, 30);
  public static readonly SpawnView EmptyView = new(Grid, [], [], []);

  public static EntityId CenterId(PlayerId owner) => owner == P1 ? new EntityId(1) : new EntityId(3);

  public static EntityId BarracksId(PlayerId owner) => owner == P1 ? new EntityId(2) : new EntityId(4);

  public static Footprint CenterFootprint(PlayerId owner) =>
      new(owner == P1 ? new GridCell(4, 4) : new GridCell(20, 20), new FootprintSize(4, 4));

  public static Footprint BarracksFootprint(PlayerId owner) =>
      new(owner == P1 ? new GridCell(12, 4) : new GridCell(20, 10), new FootprintSize(3, 3));

  public static ProductionConfig Config(int radius = DefaultRadius) => ContractsTests.ValidConfig() with
  {
    TrainingDurationTicks = new Dictionary<UnitType, int> { [UnitType.Worker] = WorkerDuration, [UnitType.Soldier] = SoldierDuration },
    MaxPendingPerProducer = MaxPending,
    SpawnSearchRadiusCells = radius,
  };

  public static IReadOnlyDictionary<UnitType, int> Costs(int workerCost = WorkerCost) =>
      new Dictionary<UnitType, int> { [UnitType.Worker] = workerCost, [UnitType.Soldier] = SoldierCost };

  public static int CostOf(UnitType unitType) => unitType == UnitType.Worker ? WorkerCost : SoldierCost;

  public static ProductionRules Rules(int radius = DefaultRadius) => new(Config(radius));

  public static ProductionCalculator Calculator(int radius = DefaultRadius, int workerCost = WorkerCost) =>
      new(Rules(radius), Costs(workerCost));

  public static ProductionRequest WorkerRequest(long sequence, PlayerId owner) =>
      new(new CommandSequence(sequence), CenterId(owner), owner, UnitType.Worker);

  public static ProductionRequest SoldierRequest(long sequence, PlayerId owner) =>
      new(new CommandSequence(sequence), BarracksId(owner), owner, UnitType.Soldier);

  public static ProducerState Center(PlayerId owner, params ProductionRequest[] pending) =>
      new(CenterId(owner), owner, BuildingType.MainCenter, CenterFootprint(owner), TickId.Zero, null, [.. pending]);

  public static ProducerState Barracks(PlayerId owner, params ProductionRequest[] pending) =>
      new(BarracksId(owner), owner, BuildingType.Barracks, BarracksFootprint(owner), TickId.Zero, null, [.. pending]);

  /// <summary>El mismo productor, con una tarea activa (ya pagada) a la que le quedan <paramref name="remaining"/> ticks.</summary>
  public static ProducerState WithJob(ProducerState producer, ProductionRequest active, int remaining, params ProductionRequest[] pending) =>
      new(
          producer.ProducerId, producer.Owner, producer.ProducerType, producer.Footprint, producer.OperationalFromTick,
          new ProductionJob(active, CostOf(active.UnitType), TickId.Zero, remaining), [.. pending]);

  /// <summary>El mismo productor, pero operativo recién desde <paramref name="tick"/> (un edificio completado en N opera desde N+1).</summary>
  public static ProducerState OperationalFrom(ProducerState producer, long tick) =>
      new(producer.ProducerId, producer.Owner, producer.ProducerType, producer.Footprint, new TickId(tick), producer.ActiveJob, producer.Pending);

  public static ProductionTickInput Input(
      long tick, ProducerState[] producers, int foodP1 = PlentyOfFood, int foodP2 = PlentyOfFood, SpawnView? view = null) =>
      new(new TickId(tick), [.. producers], [new PlayerFood(P1, foodP1), new PlayerFood(P2, foodP2)], view ?? EmptyView);

  public static SpawnView ViewWithStructures(IEnumerable<GridCell> cells) => new(Grid, cells, [], []);

  /// <summary>Distancia de Chebyshev de una celda al rectángulo de una huella (0 si está adentro).</summary>
  public static int Chebyshev(GridCell cell, Footprint footprint)
  {
    int columns = Math.Max(Math.Max(footprint.MinColumn - cell.Column, 0), cell.Column - (footprint.EndColumn - 1));
    int rows = Math.Max(Math.Max(footprint.MinRow - cell.Row, 0), cell.Row - (footprint.EndRow - 1));
    return Math.Max(columns, rows);
  }

  /// <summary>Las celdas del mapa a distancia de Chebyshev entre <paramref name="from"/> y <paramref name="to"/> de la huella.</summary>
  public static List<GridCell> CellsAround(Footprint footprint, int from, int to) =>
      Enumerable.Range(0, Grid.Rows)
          .SelectMany(row => Enumerable.Range(0, Grid.Columns).Select(column => new GridCell(column, row)))
          .Where(cell => Chebyshev(cell, footprint) >= from && Chebyshev(cell, footprint) <= to)
          .ToList();
}

/// <summary>
/// Un Simulation de juguete: aplica el resultado de cada tick como lo haría el real (actualiza los productores y descuenta la
/// comida de cada inicio), para probar escenarios de varios ticks.
/// </summary>
internal sealed class ProductionSim
{
  private readonly ProductionCalculator _calculator;
  private readonly Dictionary<EntityId, ProducerState> _producers;

  public ProductionSim(ProductionCalculator calculator, long startTick, ProducerState[] producers, int foodP1, int foodP2 = Fixtures.PlentyOfFood)
  {
    _calculator = calculator;
    _producers = producers.ToDictionary(producer => producer.ProducerId);
    Tick = new TickId(startTick);
    Food = new Dictionary<PlayerId, int> { [Fixtures.P1] = foodP1, [Fixtures.P2] = foodP2 };
  }

  public TickId Tick { get; private set; }

  public Dictionary<PlayerId, int> Food { get; }

  public SpawnView View { get; set; } = Fixtures.EmptyView;

  public ProducerState Producer(EntityId id) => _producers[id];

  /// <summary>Calcula el tick actual, confirma el resultado y avanza al siguiente.</summary>
  public ProductionTickResult Step()
  {
    var food = new[] { new PlayerFood(Fixtures.P1, Food[Fixtures.P1]), new PlayerFood(Fixtures.P2, Food[Fixtures.P2]) };
    ProductionTickResult result = _calculator.CalculateTick(new ProductionTickInput(Tick, [.. _producers.Values], [.. food], View));

    foreach (ProducerState update in result.ProducerUpdates)
    {
      _producers[update.ProducerId] = update;
    }

    foreach (ProductionStart start in result.Starts)
    {
      Food[start.Owner] -= start.PaidFood;
    }

    Tick = Tick.Next();
    return result;
  }

  public List<ProductionTickResult> Steps(int count) => Enumerable.Range(0, count).Select(_ => Step()).ToList();
}
