using Rts.Shared.Ids;

namespace Rts.Shared.Tests;

public class IdsTests
{
    [Fact]
    public void EntityId_OrdersByValue_ForDeterministicTieBreaks()
    {
        EntityId[] ids = [new(7), new(2), new(5)];

        Array.Sort(ids);

        Assert.Equal([new EntityId(2), new EntityId(5), new EntityId(7)], ids);
        Assert.True(new EntityId(2) < new EntityId(5));
    }

    [Fact]
    public void EntityId_DefaultIsNone()
    {
        Assert.True(default(EntityId).IsNone);
        Assert.False(new EntityId(1).IsNone);
    }

    [Fact]
    public void PlayerId_OnlyOneAndTwoAreValid()
    {
        Assert.True(PlayerId.One.IsValid);
        Assert.True(PlayerId.Two.IsValid);
        Assert.False(new PlayerId(0).IsValid);
        Assert.False(new PlayerId(3).IsValid);
    }

    [Fact]
    public void PlayerId_OpponentIsTheOtherPlayer()
    {
        Assert.Equal(PlayerId.Two, PlayerId.One.Opponent());
        Assert.Equal(PlayerId.One, PlayerId.Two.Opponent());
        Assert.Throws<InvalidOperationException>(() => new PlayerId(3).Opponent());
    }

    [Fact]
    public void TickId_ArithmeticAndComparison()
    {
        TickId start = TickId.Zero;
        TickId later = start.Plus(10);

        Assert.Equal(new TickId(1), start.Next());
        Assert.Equal(10, later - start);
        Assert.True(later > start);
        Assert.True(start <= TickId.Zero);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(ClientCommandId.MaxValue, true)]
    [InlineData(ClientCommandId.MaxValue + 1, false)]
    public void ClientCommandId_ValidRangeIsOneTo2Pow53(long value, bool expected)
    {
        Assert.Equal(expected, new ClientCommandId(value).IsInValidRange);
    }

    [Fact]
    public void ClientCommandId_MaxValueIsExactlyRepresentableAsDouble()
    {
        Assert.Equal(ClientCommandId.MaxValue, (long)(double)ClientCommandId.MaxValue);
    }

    [Fact]
    public void IdempotencyKey_SameClientIdFromDifferentPlayersIsDifferent()
    {
        var fromP1 = new IdempotencyKey(PlayerId.One, new ClientCommandId(42));
        var fromP2 = new IdempotencyKey(PlayerId.Two, new ClientCommandId(42));

        Assert.NotEqual(fromP1, fromP2);
        Assert.Equal(fromP1, new IdempotencyKey(PlayerId.One, new ClientCommandId(42)));
    }

    [Fact]
    public void SequentialIds_NextIncrements()
    {
        Assert.Equal(new CommandSequence(2), CommandSequence.First.Next());
        Assert.Equal(new MapRevision(1), MapRevision.Initial.Next());
        Assert.Equal(new OrderId(1), OrderId.None.Next());
        Assert.Equal(new InitializationId(2), InitializationId.First.Next());
        Assert.True(new MapRevision(3) > new MapRevision(2));
    }
}
