using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Simulation.Map;
using Rts.Simulation.State;

namespace Rts.Measurement;

/// <summary>Una orden lista para encolar: quién la manda y qué comando es.</summary>
internal sealed record PlayerOrder(PlayerId Player, IGameCommand Command);

/// <summary>
/// Escenario de carga del barrido (D-101). El mapa se arma con bloques de 4 celdas en fila, todo <b>ya en rango</b> para no depender
/// de que Movement camine:
/// <code>
/// [ árbol ][ trabajador ][ soldado P1 ][ soldado P2 ]
///             recolecta     └── se atacan entre sí ──┘
/// </code>
/// Cada bloque suma 3 unidades: un trabajador para Economy (alterna P1 y P2 para que la carga sea pareja) y dos soldados para
/// Combat. Las filas de arriba quedan para los dos Centros Principales.
/// </summary>
internal sealed class LoadScenario
{
  public const int UnitsPerBlock = 3;

  private readonly BlockLayout _layout;

  private LoadScenario(BlockLayout layout, MapDefinition map)
  {
    _layout = layout;
    Map = map;
  }

  public MapDefinition Map { get; }

  /// <summary>Cuántas órdenes da <see cref="OrdersFor"/>: una por unidad (recolectar o atacar).</summary>
  public int OrderCount => _layout.Blocks * UnitsPerBlock;

  /// <param name="units">Cantidad de unidades: múltiplo de <see cref="UnitsPerBlock"/>.</param>
  public static LoadScenario Create(int units, FootprintSize mainCenterSize)
  {
    if (units <= 0 || units % UnitsPerBlock != 0)
    {
      throw new ArgumentOutOfRangeException(nameof(units), units, $"Tiene que ser un múltiplo positivo de {UnitsPerBlock}.");
    }

    var layout = new BlockLayout(units / UnitsPerBlock, mainCenterSize);
    MapDefinition map = layout.BuildMap();
    IReadOnlyList<string> errors = map.GetValidationErrors(mainCenterSize);
    if (errors.Count > 0)
    {
      throw new InvalidOperationException($"El mapa de carga es inválido: {string.Join(" ", errors)}");
    }

    return new LoadScenario(layout, map);
  }

  /// <summary>Las órdenes del escenario, con los IDs que asignó el mundo inicial: 3 por bloque (recolectar y dos ataques).</summary>
  public IReadOnlyList<PlayerOrder> OrdersFor(WorldState world)
  {
    Dictionary<PositionXZ, EntityId> units = world.Units.Values.ToDictionary(unit => unit.Position, unit => unit.Id);
    Dictionary<GridCell, EntityId> trees = world.Trees.Values.ToDictionary(tree => tree.Cell, tree => tree.Id);
    var orders = new List<PlayerOrder>(_layout.Blocks * UnitsPerBlock);
    for (int block = 0; block < _layout.Blocks; block++)
    {
      EntityId worker = units[_layout.WorkerPosition(block)];
      EntityId p1Soldier = units[_layout.SoldierPosition(block, PlayerId.One)];
      EntityId p2Soldier = units[_layout.SoldierPosition(block, PlayerId.Two)];
      EntityId tree = trees[_layout.TreeCell(block)];
      orders.Add(new PlayerOrder(BlockLayout.WorkerOwner(block), new GatherWoodCommand(tree, world.MapRevision, StayInPlace(worker))));
      orders.Add(new PlayerOrder(PlayerId.One, new AttackCommand(p2Soldier, world.MapRevision, StayInPlace(p1Soldier))));
      orders.Add(new PlayerOrder(PlayerId.Two, new AttackCommand(p1Soldier, world.MapRevision, StayInPlace(p2Soldier))));
    }

    return orders;
  }

  /// <summary>Ruta vacía: la unidad ya está en rango de su objetivo, así que la orden se acepta sin moverse.</summary>
  private static EquatableArray<UnitRouteCandidate> StayInPlace(EntityId unit) => [new UnitRouteCandidate(unit, [])];

  /// <summary>Dónde va cada cosa: bloques de 4 celdas, 16 por fila, debajo de las filas de los Centros Principales.</summary>
  private sealed class BlockLayout(int blocks, FootprintSize mainCenterSize)
  {
    private const int CellsPerBlock = 4;
    private const int BlocksPerRow = 16;
    private const int Columns = CellsPerBlock * BlocksPerRow;
    private const double CellSize = 1.0;
    private const double ToCellCenter = 0.5;
    private const int WorkerOffset = 1;
    private const int P1SoldierOffset = 2;
    private const int P2SoldierOffset = 3;
    private const int MarginCells = 1;

    private readonly int _firstBlockRow = MarginCells + mainCenterSize.Height + MarginCells;

    public int Blocks => blocks;

    public static PlayerId WorkerOwner(int block) => block % 2 == 0 ? PlayerId.One : PlayerId.Two;

    public MapDefinition BuildMap()
    {
      int rows = _firstBlockRow + ((blocks + BlocksPerRow - 1) / BlocksPerRow) + MarginCells;
      var p2Anchor = new GridCell(Columns - mainCenterSize.Width - MarginCells, MarginCells);
      return new MapDefinition(
          new GridSpec(CellSize, Columns, rows),
          [.. Enumerable.Range(0, blocks).Select(TreeCell)],
          [
            new PlayerStartDefinition(PlayerId.One, new GridCell(MarginCells, MarginCells), UnitsOf(PlayerId.One)),
            new PlayerStartDefinition(PlayerId.Two, p2Anchor, UnitsOf(PlayerId.Two)),
          ]);
    }

    public GridCell TreeCell(int block) => new((block % BlocksPerRow) * CellsPerBlock, _firstBlockRow + (block / BlocksPerRow));

    public PositionXZ WorkerPosition(int block) => PositionAt(block, WorkerOffset);

    public PositionXZ SoldierPosition(int block, PlayerId owner) =>
        PositionAt(block, owner == PlayerId.One ? P1SoldierOffset : P2SoldierOffset);

    private EquatableArray<InitialUnitDefinition> UnitsOf(PlayerId player)
    {
      IEnumerable<InitialUnitDefinition> soldiers = Enumerable.Range(0, blocks)
          .Select(block => new InitialUnitDefinition(UnitType.Soldier, SoldierPosition(block, player)));
      IEnumerable<InitialUnitDefinition> workers = Enumerable.Range(0, blocks)
          .Where(block => WorkerOwner(block) == player)
          .Select(block => new InitialUnitDefinition(UnitType.Worker, WorkerPosition(block)));
      return [.. soldiers.Concat(workers)];
    }

    private PositionXZ PositionAt(int block, int offset)
    {
      GridCell tree = TreeCell(block);
      return new PositionXZ((tree.Column + offset + ToCellCenter) * CellSize, (tree.Row + ToCellCenter) * CellSize);
    }
  }
}
