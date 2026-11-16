using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Simulation.Commands;

namespace Rts.Simulation.Tests.Commands;

public class CommandQueueTests
{
  private const int SmallCapacity = 3;
  private const int LargeCapacity = 1_000;
  private const int BatchCommandCount = 100;
  private const int MaxCommandsPerTick = 30;
  private const int CommandsPerPlayer = 2_000;
  private const int ConcurrentCapacity = CommandsPerPlayer * 2;
  private static readonly TimeSpan ConcurrencyTimeout = TimeSpan.FromSeconds(10);

  private static readonly IGameCommand AnyCommand = new StopCommand([new EntityId(1)]);

  private static ClientCommandId ClientId(long value) => new(value);

  [Fact]
  public void Admit_AssignsConsecutiveSequencesStartingAtFirst()
  {
    var queue = new CommandQueue(SmallCapacity);

    CommandAdmission first = queue.Admit(PlayerId.One, ClientId(1), AnyCommand);
    CommandAdmission second = queue.Admit(PlayerId.Two, ClientId(1), AnyCommand);

    Assert.Equal(CommandSequence.First, first.Sequence);
    Assert.Equal(CommandSequence.First.Next(), second.Sequence);
  }

  [Fact]
  public void DequeueBatch_KeepsPlayerClientIdAndCommand()
  {
    var queue = new CommandQueue(SmallCapacity);
    var clientId = ClientId(42);
    queue.Admit(PlayerId.Two, clientId, AnyCommand);

    CommandEnvelope envelope = Assert.Single(queue.DequeueBatch(MaxCommandsPerTick));

    Assert.Equal(PlayerId.Two, envelope.Player);
    Assert.Equal(clientId, envelope.ClientCommandId);
    Assert.Same(AnyCommand, envelope.Command);
  }

  [Fact]
  public void DequeueBatch_OnEmptyQueue_ReturnsEmpty()
  {
    var queue = new CommandQueue(SmallCapacity);

    Assert.Empty(queue.DequeueBatch(MaxCommandsPerTick));
  }

  [Fact]
  public void DequeueBatch_HundredWithLimitThirty_LeavesSeventyPending()
  {
    // Prueba obligatoria del contrato 06 §11: lote de 100 con límite 30 deja 70 pendientes.
    var queue = new CommandQueue(LargeCapacity);
    for (int i = 1; i <= BatchCommandCount; i++)
    {
      queue.Admit(PlayerId.One, ClientId(i), AnyCommand);
    }

    IReadOnlyList<CommandEnvelope> batch = queue.DequeueBatch(MaxCommandsPerTick);

    Assert.Equal(MaxCommandsPerTick, batch.Count);
    Assert.Equal(BatchCommandCount - MaxCommandsPerTick, queue.PendingCount);
    AssertConsecutiveFrom(CommandSequence.First, batch);
    AssertConsecutiveFrom(batch[^1].Sequence.Next(), queue.DequeueBatch(LargeCapacity));
  }

  [Fact]
  public void Admit_WhenFull_RejectsWithoutDroppingAdmitted()
  {
    var queue = new CommandQueue(SmallCapacity);
    for (int i = 1; i <= SmallCapacity; i++)
    {
      Assert.True(queue.Admit(PlayerId.One, ClientId(i), AnyCommand).IsAdmitted);
    }

    CommandAdmission overflow = queue.Admit(PlayerId.Two, ClientId(1), AnyCommand);

    Assert.False(overflow.IsAdmitted);
    Assert.Equal(CommonErrors.CommandQueueFull, overflow.Error);
    Assert.Equal(SmallCapacity, queue.PendingCount);
  }

  [Fact]
  public void Admit_Rejected_DoesNotConsumeSequence()
  {
    var queue = new CommandQueue(SmallCapacity);
    for (int i = 1; i <= SmallCapacity; i++)
    {
      queue.Admit(PlayerId.One, ClientId(i), AnyCommand);
    }

    queue.Admit(PlayerId.Two, ClientId(1), AnyCommand);
    queue.DequeueBatch(SmallCapacity);
    CommandAdmission afterDrain = queue.Admit(PlayerId.Two, ClientId(2), AnyCommand);

    Assert.Equal(new CommandSequence(SmallCapacity + 1), afterDrain.Sequence);
  }

  [Fact]
  public void InvalidArguments_Throw()
  {
    var queue = new CommandQueue(SmallCapacity);

    Assert.Throws<ArgumentOutOfRangeException>(() => new CommandQueue(0));
    Assert.Throws<ArgumentOutOfRangeException>(() => queue.DequeueBatch(0));
    Assert.Throws<ArgumentNullException>(() => queue.Admit(PlayerId.One, ClientId(1), null!));
    Assert.Throws<ArgumentException>(() => queue.Admit(new PlayerId(3), ClientId(1), AnyCommand));
  }

  [Fact]
  public async Task ConcurrentProducers_SequencesAreUniqueOrderedAndPerPlayerOrderIsKept()
  {
    // Prueba de integración AGENTS §7 (lado cola): dos receptores admiten a la vez; secuencias únicas, sin huecos,
    // en orden de cola y sin perder ni duplicar comandos.
    var queue = new CommandQueue(ConcurrentCapacity);
    using var start = new Barrier(participantCount: 2);

    Task p1 = Task.Run(() => AdmitMany(queue, PlayerId.One, start));
    Task p2 = Task.Run(() => AdmitMany(queue, PlayerId.Two, start));
    await Task.WhenAll(p1, p2).WaitAsync(ConcurrencyTimeout);

    IReadOnlyList<CommandEnvelope> all = queue.DequeueBatch(ConcurrentCapacity);
    Assert.Equal(ConcurrentCapacity, all.Count);
    AssertConsecutiveFrom(CommandSequence.First, all);
    AssertClientOrderPerPlayer(all, PlayerId.One);
    AssertClientOrderPerPlayer(all, PlayerId.Two);
  }

  [Fact]
  public async Task ConcurrentProducerAndConsumer_EveryCommandIsReceivedOnceInOrder()
  {
    var queue = new CommandQueue(ConcurrentCapacity);
    using var start = new Barrier(participantCount: 2);
    Task producer = Task.Run(() => AdmitMany(queue, PlayerId.One, start));
    Task<List<CommandEnvelope>> consumer = Task.Run(() => DrainUntil(queue, CommandsPerPlayer, start));

    await producer.WaitAsync(ConcurrencyTimeout);
    List<CommandEnvelope> received = await consumer.WaitAsync(ConcurrencyTimeout);

    Assert.Equal(CommandsPerPlayer, received.Count);
    AssertConsecutiveFrom(CommandSequence.First, received);
  }

  private static void AdmitMany(CommandQueue queue, PlayerId player, Barrier start)
  {
    start.SignalAndWait();
    for (int i = 1; i <= CommandsPerPlayer; i++)
    {
      Assert.True(queue.Admit(player, ClientId(i), AnyCommand).IsAdmitted);
    }
  }

  private static List<CommandEnvelope> DrainUntil(CommandQueue queue, int expected, Barrier start)
  {
    var received = new List<CommandEnvelope>();
    start.SignalAndWait();
    while (received.Count < expected)
    {
      received.AddRange(queue.DequeueBatch(MaxCommandsPerTick));
    }

    return received;
  }

  private static void AssertConsecutiveFrom(CommandSequence first, IReadOnlyList<CommandEnvelope> envelopes)
  {
    CommandSequence expected = first;
    foreach (CommandEnvelope envelope in envelopes)
    {
      Assert.Equal(expected, envelope.Sequence);
      expected = expected.Next();
    }
  }

  private static void AssertClientOrderPerPlayer(IReadOnlyList<CommandEnvelope> envelopes, PlayerId player)
  {
    long[] clientIds = envelopes.Where(e => e.Player == player).Select(e => e.ClientCommandId.Value).ToArray();
    Assert.Equal(Enumerable.Range(1, CommandsPerPlayer).Select(i => (long)i), clientIds);
  }
}
