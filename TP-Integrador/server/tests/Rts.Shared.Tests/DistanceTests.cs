using Rts.Shared.Geometry;

namespace Rts.Shared.Tests;

public class DistanceTests
{
  private static readonly WorldRect Rect = new(minX: 2, minZ: 2, maxX: 6, maxZ: 4);

  [Fact]
  public void GridSpec_ToWorldRect_CoversWholeCells()
  {
    var grid = new GridSpec(cellSize: 2.0, columns: 10, rows: 10);
    var footprint = new Footprint(new GridCell(1, 1), new FootprintSize(2, 1));

    Assert.Equal(new WorldRect(2, 2, 6, 4), grid.ToWorldRect(footprint));
  }

  [Fact]
  public void WorldRect_RejectsInvertedOrNonFinite()
  {
    Assert.Throws<ArgumentException>(() => new WorldRect(5, 0, 1, 1));
    Assert.Throws<ArgumentException>(() => new WorldRect(0, double.NaN, 1, 1));
  }

  [Fact]
  public void WorldRect_PointInsideHasZeroDistance()
  {
    Assert.Equal(0, Rect.DistanceTo(new PositionXZ(3, 3)));
    Assert.Equal(0, Rect.DistanceTo(new PositionXZ(6, 4)));
  }

  [Theory]
  [InlineData(0, 3, 2)]
  [InlineData(9, 3, 3)]
  [InlineData(4, 0, 2)]
  [InlineData(4, 7, 3)]
  [InlineData(9, 8, 5)]
  public void WorldRect_DistanceIsToNearestEdge(double x, double z, double expected)
  {
    Assert.Equal(expected, Rect.DistanceTo(new PositionXZ(x, z)), precision: 12);
  }

  [Fact]
  public void WorldRect_ClosestPointIsClampedPosition()
  {
    Assert.Equal(new PositionXZ(6, 2), Rect.ClosestPointTo(new PositionXZ(10, -5)));
    Assert.Equal(new PositionXZ(4, 3), Rect.Center);
  }

  [Fact]
  public void Ranges_AreInclusive()
  {
    Assert.True(Rect.IsWithinRange(new PositionXZ(9, 3), 3));
    Assert.False(Rect.IsWithinRange(new PositionXZ(9, 3), 2.999));
    Assert.True(new PositionXZ(0, 0).IsWithinRange(new PositionXZ(3, 4), 5));
    Assert.False(new PositionXZ(0, 0).IsWithinRange(new PositionXZ(3, 4), 4.999));
  }
}
