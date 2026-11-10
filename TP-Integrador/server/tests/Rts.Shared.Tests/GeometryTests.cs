using Rts.Shared.Geometry;

namespace Rts.Shared.Tests;

public class GeometryTests
{
  private static readonly GridSpec Grid = new(cellSize: 2.0, columns: 10, rows: 8);

  [Fact]
  public void GridSpec_WorldSizeDerivesFromCells()
  {
    Assert.Equal(20.0, Grid.WorldWidth);
    Assert.Equal(16.0, Grid.WorldDepth);
  }

  [Theory]
  [InlineData(0.0, 0.0, 0, 0)]
  [InlineData(1.999, 0.0, 0, 0)]
  [InlineData(2.0, 0.0, 1, 0)]
  [InlineData(19.999, 15.999, 9, 7)]
  [InlineData(5.0, 3.0, 2, 1)]
  public void GridSpec_ToCell_UsesFloorOfPositionOverCellSize(double x, double z, int column, int row)
  {
    Assert.Equal(new GridCell(column, row), Grid.ToCell(new PositionXZ(x, z)));
  }

  [Fact]
  public void GridSpec_ToCell_OutsidePositionsMapOutsideGrid()
  {
    GridCell negative = Grid.ToCell(new PositionXZ(-0.5, 0));
    GridCell edge = Grid.ToCell(new PositionXZ(20.0, 0));

    Assert.Equal(new GridCell(-1, 0), negative);
    Assert.False(Grid.Contains(negative));
    Assert.False(Grid.Contains(edge));
  }

  [Fact]
  public void GridSpec_ToCell_PositionJustInsideEdgeStaysInsideDespiteRounding()
  {
    var grid = new GridSpec(cellSize: 0.1, columns: 100, rows: 100);
    var position = new PositionXZ(Math.BitDecrement(grid.WorldWidth), 0);

    Assert.True(grid.Contains(position));
    Assert.True(grid.Contains(grid.ToCell(position)));
  }

  [Fact]
  public void GridSpec_ToCell_RejectsNonFinitePositions()
  {
    Assert.Throws<ArgumentException>(() => Grid.ToCell(new PositionXZ(double.NaN, 0)));
    Assert.False(Grid.Contains(new PositionXZ(double.PositiveInfinity, 0)));
  }

  [Fact]
  public void GridSpec_BoundsAreHalfOpen()
  {
    Assert.True(Grid.Contains(new PositionXZ(0, 0)));
    Assert.False(Grid.Contains(new PositionXZ(20.0, 1)));
    Assert.False(Grid.Contains(new PositionXZ(1, 16.0)));
  }

  [Fact]
  public void GridSpec_CellCenter()
  {
    Assert.Equal(new PositionXZ(5.0, 3.0), Grid.CellCenter(new GridCell(2, 1)));
  }

  [Fact]
  public void GridSpec_RejectsInvalidDimensions()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new GridSpec(0, 10, 10));
    Assert.Throws<ArgumentOutOfRangeException>(() => new GridSpec(double.NaN, 10, 10));
    Assert.Throws<ArgumentOutOfRangeException>(() => new GridSpec(1, 0, 10));
    Assert.Throws<ArgumentOutOfRangeException>(() => new GridSpec(1, 10, -1));
  }

  [Fact]
  public void GridSpec_ContainsFootprintOnlyIfFullyInside()
  {
    Assert.True(Grid.Contains(new Footprint(new GridCell(8, 6), new FootprintSize(2, 2))));
    Assert.False(Grid.Contains(new Footprint(new GridCell(9, 6), new FootprintSize(2, 2))));
    Assert.False(Grid.Contains(new Footprint(new GridCell(-1, 0), new FootprintSize(1, 1))));
  }

  [Fact]
  public void FootprintSize_RequiresPositiveDimensions()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new FootprintSize(0, 1));
    Assert.Throws<ArgumentOutOfRangeException>(() => new FootprintSize(1, -2));
    Assert.Equal(6, new FootprintSize(3, 2).CellCount);
  }

  [Fact]
  public void Footprint_CellsAreEnumeratedRowMajorAscending()
  {
    var footprint = new Footprint(new GridCell(3, 5), new FootprintSize(2, 2));

    Assert.Equal(
        [new GridCell(3, 5), new GridCell(4, 5), new GridCell(3, 6), new GridCell(4, 6)],
        footprint.Cells());
  }

  [Fact]
  public void Footprint_ContainsUsesExclusiveEnd()
  {
    var footprint = new Footprint(new GridCell(3, 5), new FootprintSize(2, 3));

    Assert.True(footprint.Contains(new GridCell(4, 7)));
    Assert.False(footprint.Contains(new GridCell(5, 5)));
    Assert.False(footprint.Contains(new GridCell(3, 8)));
  }

  [Fact]
  public void Footprint_IntersectsOnlyWhenSharingCells()
  {
    var a = new Footprint(new GridCell(0, 0), new FootprintSize(2, 2));
    var overlapping = new Footprint(new GridCell(1, 1), new FootprintSize(2, 2));
    var adjacent = new Footprint(new GridCell(2, 0), new FootprintSize(1, 1));

    Assert.True(a.Intersects(overlapping));
    Assert.True(overlapping.Intersects(a));
    Assert.False(a.Intersects(adjacent));
  }

  [Fact]
  public void PositionXZ_Distance()
  {
    var a = new PositionXZ(1, 1);
    var b = new PositionXZ(4, 5);

    Assert.Equal(25.0, a.DistanceSquaredTo(b));
    Assert.Equal(5.0, a.DistanceTo(b));
  }
}
