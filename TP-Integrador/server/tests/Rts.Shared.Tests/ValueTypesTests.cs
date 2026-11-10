using Rts.Shared.Collections;
using Rts.Shared.Errors;
using Rts.Shared.Resources;
using Rts.Shared.Time;

namespace Rts.Shared.Tests;

public class ValueTypesTests
{
  [Fact]
  public void ResourceAmount_Arithmetic()
  {
    var balance = new ResourceAmount(Wood: 10, Food: 4);
    var cost = ResourceAmount.OfWood(3);

    Assert.Equal(new ResourceAmount(7, 4), balance - cost);
    Assert.Equal(new ResourceAmount(13, 4), balance + cost);
    Assert.Equal(new ResourceAmount(-3, 0), -cost);
    Assert.True(ResourceAmount.Zero.IsZero);
  }

  [Fact]
  public void ResourceAmount_DetectsNegativeComponents()
  {
    Assert.True(new ResourceAmount(0, 0).IsNonNegative);
    Assert.False((ResourceAmount.OfFood(2) - ResourceAmount.OfFood(3)).IsNonNegative);
  }

  [Fact]
  public void ResourceAmount_OverflowThrows()
  {
    Assert.Throws<OverflowException>(() => ResourceAmount.OfWood(int.MaxValue) + ResourceAmount.OfWood(1));
  }

  [Fact]
  public void TickRate_DefaultIsTwentyHertzAndFiftyMilliseconds()
  {
    Assert.Equal(20, TickRate.Default.TicksPerSecond);
    Assert.Equal(TimeSpan.FromMilliseconds(50), TickRate.Default.TickInterval);
  }

  [Fact]
  public void TickRate_PerTickConvertsFromPerSecond()
  {
    Assert.Equal(0.25, TickRate.Default.PerTick(5.0));
    Assert.Equal(0.5, new TickRate(10).PerTick(5.0));
  }

  [Fact]
  public void TickRate_RejectsInvalidValues()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new TickRate(0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new TickRate(-20));
  }

  [Theory]
  [InlineData("Common.NotOwner")]
  [InlineData("Economy.InsufficientWood")]
  [InlineData("Movement.InvalidPath2")]
  public void ErrorCode_AcceptsAreaDotName(string value)
  {
    var code = new ErrorCode(value);

    Assert.Equal(value, code.Value);
    Assert.Equal(value[..value.IndexOf('.')], code.Area);
  }

  [Theory]
  [InlineData("")]
  [InlineData("NotOwner")]
  [InlineData("common.NotOwner")]
  [InlineData("Common.")]
  [InlineData("Common.Not Owner")]
  [InlineData("A.B.C")]
  public void ErrorCode_RejectsInvalidFormat(string value)
  {
    Assert.Throws<ArgumentException>(() => new ErrorCode(value));
  }

  [Fact]
  public void ErrorCode_EqualityIsByValue()
  {
    Assert.Equal(CommonErrors.NotOwner, new ErrorCode("Common.NotOwner"));
    Assert.NotEqual(CommonErrors.NotOwner, CommonErrors.EntityNotFound);
    Assert.True(default(ErrorCode).IsDefault);
  }

  [Fact]
  public void EquatableArray_HasStructuralEquality()
  {
    EquatableArray<int> a = [1, 2, 3];
    EquatableArray<int> b = new[] { 1, 2, 3 }.ToEquatableArray();
    EquatableArray<int> c = [3, 2, 1];

    Assert.Equal(a, b);
    Assert.Equal(a.GetHashCode(), b.GetHashCode());
    Assert.NotEqual(a, c);
  }

  [Fact]
  public void EquatableArray_DefaultBehavesAsEmpty()
  {
    EquatableArray<int> empty = default;

    Assert.True(empty.IsEmpty);
    Assert.Equal(EquatableArray<int>.Empty, empty);
    Assert.Empty(empty);
  }

  [Fact]
  public void EquatableArray_MakesContainingRecordsStructurallyEqual()
  {
    var first = new Holder("x", [1, 2]);
    var second = new Holder("x", [1, 2]);

    Assert.Equal(first, second);
  }

  private sealed record Holder(string Name, EquatableArray<int> Values);
}
