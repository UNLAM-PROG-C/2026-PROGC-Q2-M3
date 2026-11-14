using Rts.Production.Model;
using Rts.Shared.Geometry;
using static Rts.Production.Tests.Fixtures;

namespace Rts.Production.Tests;

/// <summary>
/// Búsqueda de la celda de aparición (contrato 05 §4, D-039 y D-040). Con la huella de 4×4 en la columna 4 y fila 4, el anillo 1
/// es el borde del cuadrado [3..8]×[3..8]. Las celdas del medio de cada lado están todas a la misma distancia del centro, así que
/// el desempate por fila y columna fija el orden: (5,3), (6,3), (3,5), (8,5), (3,6), (8,6), (5,8), (6,8).
/// </summary>
public class SpawnCellTests
{
  private static readonly Footprint Center = CenterFootprint(P1);
  private static readonly IReadOnlySet<GridCell> NoneReserved = new HashSet<GridCell>();

  private static readonly GridCell[] RingOneTieOrder =
  [
    new(5, 3), new(6, 3), new(3, 5), new(8, 5), new(3, 6), new(8, 6), new(5, 8), new(6, 8),
  ];

  private static GridCell? Find(SpawnView view, int radius = DefaultRadius, IReadOnlySet<GridCell>? reserved = null, Footprint? footprint = null) =>
      Rules(radius).FindSpawnCell(footprint ?? Center, view, reserved ?? NoneReserved);

  [Fact]
  public void WithEverythingFree_ItPicksTheRingOneCellNearestTheCenter_BreakingTiesByRowThenColumn()
  {
    Assert.Equal(new GridCell(5, 3), Find(EmptyView));
  }

  [Fact]
  public void BlockingEachChoiceInTurn_WalksTheTieOrderOfTheRing()
  {
    var blocked = new List<GridCell>();
    var chosen = new List<GridCell>();

    foreach (GridCell expected in RingOneTieOrder)
    {
      GridCell? cell = Find(ViewWithStructures(blocked));
      chosen.Add(cell!.Value);
      blocked.Add(expected);
    }

    Assert.Equal(RingOneTieOrder, chosen);
  }

  [Fact]
  public void CellsWithStructuresTreesOrUnits_AreSkipped()
  {
    var view = new SpawnView(Grid, [new GridCell(5, 3)], [new GridCell(6, 3)], [new GridCell(3, 5)]);

    Assert.Equal(new GridCell(8, 5), Find(view));
  }

  [Fact]
  public void ACellAlreadyReservedByAnotherSpawnInTheSameTick_IsSkipped()
  {
    var reserved = new HashSet<GridCell> { new(5, 3), new(6, 3) };

    Assert.Equal(new GridCell(3, 5), Find(EmptyView, reserved: reserved));
  }

  [Fact]
  public void ADepletedTreesReservedCell_IsStillOffLimits()
  {
    // Los árboles conservan sus celdas reservadas aunque estén agotados (D-040): llegan en la vista como celdas de árbol.
    var view = new SpawnView(Grid, [], RingOneTieOrder.Take(2), []);

    Assert.Equal(new GridCell(3, 5), Find(view));
  }

  [Fact]
  public void WhenRingOneIsFull_ItMovesToRingTwo()
  {
    SpawnView view = ViewWithStructures(CellsAround(Center, 1, 1));

    GridCell cell = Find(view)!.Value;

    Assert.Equal(2, Chebyshev(cell, Center));
    Assert.Equal(new GridCell(5, 2), cell);
  }

  [Fact]
  public void RingOrderBeatsDistance_ACornerOfRingOneWinsOverACloserCellOfRingTwo()
  {
    // (3,3) es la esquina del anillo 1: a distancia 50 (en unidades dobles), igual que (5,2) del anillo 2, que tiene fila menor.
    // Si ganara la distancia y la fila, saldría (5,2); gana el anillo.
    SpawnView view = ViewWithStructures(CellsAround(Center, 1, 1).Where(cell => cell != new GridCell(3, 3)));

    Assert.Equal(new GridCell(3, 3), Find(view));
  }

  [Fact]
  public void WhenTheWholeRadiusIsBlocked_ThereIsNoCell()
  {
    SpawnView view = ViewWithStructures(CellsAround(Center, 1, DefaultRadius));

    Assert.Null(Find(view));
  }

  [Fact]
  public void TheSearchNeverGoesBeyondTheConfiguredRadius()
  {
    SpawnView view = ViewWithStructures(CellsAround(Center, 1, 1));

    Assert.Null(Find(view, radius: 1));
    Assert.NotNull(Find(view, radius: 2));
  }

  [Fact]
  public void CellsOutsideTheMap_AreNeverChosen()
  {
    var corner = new Footprint(new GridCell(0, 0), new FootprintSize(4, 4));

    GridCell cell = Find(EmptyView, footprint: corner)!.Value;

    Assert.True(Grid.Contains(cell));
    Assert.Equal(1, Chebyshev(cell, corner));
  }

  [Fact]
  public void TheChoiceDoesNotDependOnTheCellSize()
  {
    var bigCells = new GridSpec(2.5, 30, 30);
    var view = new SpawnView(bigCells, [], [], []);

    GridCell? cell = Rules().FindSpawnCell(Center, view, NoneReserved);

    Assert.Equal(new GridCell(5, 3), cell);
  }

  [Fact]
  public void ANonSquareFootprint_IsMeasuredFromItsOwnCenter()
  {
    var wide = new Footprint(new GridCell(10, 10), new FootprintSize(6, 2));

    GridCell cell = Find(EmptyView, footprint: wide)!.Value;

    // Centro de la huella: columna 13, fila 11. El lado de arriba (fila 9) tiene las celdas 12 y 13 a igual distancia.
    Assert.Equal(new GridCell(12, 9), cell);
  }

  [Fact]
  public void NullArguments_AreRejected()
  {
    Assert.Throws<ArgumentNullException>(() => Rules().FindSpawnCell(Center, null!, NoneReserved));
    Assert.Throws<ArgumentNullException>(() => Rules().FindSpawnCell(Center, EmptyView, null!));
  }
}
