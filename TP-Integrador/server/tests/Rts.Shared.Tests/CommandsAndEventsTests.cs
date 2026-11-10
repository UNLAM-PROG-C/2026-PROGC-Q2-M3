using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Shared.Tests;

public class CommandsAndEventsTests
{
  private static readonly CommandEnvelope Envelope = new(
      new CommandSequence(10),
      PlayerId.One,
      new ClientCommandId(5),
      new FakeCommand());

  private static readonly TickId Tick = new(3);

  [Fact]
  public void CommandEnvelope_ExposesIdempotencyKeyAndType()
  {
    Assert.Equal(new IdempotencyKey(PlayerId.One, new ClientCommandId(5)), Envelope.Key);
    Assert.Equal(CommandType.Stop, Envelope.Type);
  }

  [Fact]
  public void CommandResult_Accepted()
  {
    CommandResult result = CommandResult.Accepted(Envelope, Tick, [EntityCommandResult.Accepted(new EntityId(1))]);

    Assert.Equal(CommandOutcome.Accepted, result.Outcome);
    Assert.Null(result.Error);
    Assert.Equal(Envelope.Key, result.Key);
    Assert.Equal(Envelope.Sequence, result.Sequence);
    Assert.Equal(Tick, result.Tick);
  }

  [Fact]
  public void CommandResult_PartiallyAcceptedRequiresMixedEntityResults()
  {
    CommandResult result = CommandResult.PartiallyAccepted(Envelope, Tick,
    [
        EntityCommandResult.Accepted(new EntityId(1)),
            EntityCommandResult.Rejected(new EntityId(2), CommonErrors.IncompatibleEntity),
        ]);

    Assert.Equal(CommandOutcome.PartiallyAccepted, result.Outcome);
    Assert.Throws<ArgumentException>(() =>
        CommandResult.PartiallyAccepted(Envelope, Tick, [EntityCommandResult.Accepted(new EntityId(1))]));
  }

  [Fact]
  public void CommandResult_RejectedRequiresErrorAndNoAcceptedEntities()
  {
    CommandResult result = CommandResult.Rejected(Envelope, Tick, CommonErrors.NoEntityAccepted,
        [EntityCommandResult.Rejected(new EntityId(2), CommonErrors.NotOwner)]);

    Assert.Equal(CommandOutcome.Rejected, result.Outcome);
    Assert.Throws<ArgumentException>(() =>
        CommandResult.Rejected(Envelope, Tick, CommonErrors.InvalidCommand,
            [EntityCommandResult.Accepted(new EntityId(1))]));
  }

  [Fact]
  public void CommandResult_AcceptedCannotContainRejectedEntities()
  {
    Assert.Throws<ArgumentException>(() =>
        CommandResult.Accepted(Envelope, Tick,
            [EntityCommandResult.Rejected(new EntityId(2), CommonErrors.NotOwner)]));
  }

  [Fact]
  public void CommandResult_RejectsDefaultErrorCode()
  {
    Assert.Throws<ArgumentException>(() => CommandResult.Rejected(Envelope, Tick, default));
  }

  [Fact]
  public void CommandResult_EqualityIsStructural_ForIdempotentReplies()
  {
    CommandResult first = CommandResult.Rejected(Envelope, Tick, CommonErrors.NoEntityAccepted,
        [EntityCommandResult.Rejected(new EntityId(2), CommonErrors.NotOwner)]);
    CommandResult second = CommandResult.Rejected(Envelope, Tick, CommonErrors.NoEntityAccepted,
        [EntityCommandResult.Rejected(new EntityId(2), CommonErrors.NotOwner)]);

    Assert.Equal(first, second);
  }

  [Fact]
  public void EventStamp_OrdersByTickThenIndex()
  {
    EventStamp[] stamps =
    [
        new(new TickId(2), 0),
            new(new TickId(1), 5),
            new(new TickId(1), 0),
        ];

    Array.Sort(stamps);

    Assert.Equal(
        [new EventStamp(new TickId(1), 0), new EventStamp(new TickId(1), 5), new EventStamp(new TickId(2), 0)],
        stamps);
  }

  [Fact]
  public void DomainEvent_EqualityIncludesPayload()
  {
    var stamp = new EventStamp(Tick, 0);

    Assert.Equal(new DomainEvent(stamp, new FakeEvent(1)), new DomainEvent(stamp, new FakeEvent(1)));
    Assert.NotEqual(new DomainEvent(stamp, new FakeEvent(1)), new DomainEvent(stamp, new FakeEvent(2)));
  }

  private sealed record FakeCommand : IGameCommand
  {
    public CommandType Type => CommandType.Stop;
  }

  private sealed record FakeEvent(int Value) : IDomainEventPayload;
}
