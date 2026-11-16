using Rts.Networking.Admission;
using Rts.Networking.Parsing;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests.Admission;

public class CommandAdmitterTests
{
  private static readonly ConnectionId Connection = new(7);

  // El jugador del gate es el 2 a propósito: así se ve que el PlayerId sale de la conexión y no de otro lado.
  private static readonly PlayerId PlayerOfTheConnection = PlayerId.Two;

  private static readonly GameplayMessage Message = new(
      new ClientCommandId(9),
      new StopCommand(EquatableArray.Create<EntityId>([new EntityId(3)])));

  private readonly FakeSessionGate _gate = new() { Player = PlayerOfTheConnection };
  private readonly FakeRateLimiter _rateLimiter = new();
  private readonly FakeCommandSink _sink = new();

  private CommandAdmitter CreateAdmitter() => new(Connection, _gate, _rateLimiter, _sink);

  [Fact]
  public void Admit_AllStepsPass_AdmitsAndReturnsTheSequence()
  {
    _sink.Result = CommandAdmission.Admitted(new CommandSequence(42));

    var decision = CreateAdmitter().Admit(Message);

    Assert.True(decision.IsAdmitted);
    Assert.Equal(new CommandSequence(42), decision.Sequence);
    Assert.Null(decision.Error);
    Assert.False(decision.IsViolation);
  }

  [Fact]
  public void Admit_AllStepsPass_SendsTheCommandToTheQueueWithThePlayerOfTheConnection()
  {
    CreateAdmitter().Admit(Message);

    var call = Assert.Single(_sink.Calls);
    Assert.Equal(PlayerOfTheConnection, call.Player);
    Assert.Equal(Message.ClientCommandId, call.ClientCommandId);
    Assert.Same(Message.Command, call.Command);
    Assert.Equal([Connection], _gate.PlayerLookups);
  }

  // Paso 1: la partida acepta comandos.
  [Fact]
  public void Admit_MatchNotAcceptingGameplay_RejectsWithMatchNotRunningAndNoViolation()
  {
    _gate.AcceptsGameplay = false;

    var decision = CreateAdmitter().Admit(Message);

    AssertRejected(decision, CommonErrors.MatchNotRunning, isViolation: false);
    Assert.Empty(_gate.PlayerLookups);
    Assert.Equal(0, _rateLimiter.Calls);
    Assert.Empty(_sink.Calls);
  }

  // Paso 2: la conexión tiene jugador.
  [Fact]
  public void Admit_ConnectionWithoutPlayer_RejectsWithNotRegisteredAndNoViolation()
  {
    _gate.Player = null;

    var decision = CreateAdmitter().Admit(Message);

    AssertRejected(decision, NetworkErrors.NotRegistered, isViolation: false);
    Assert.Equal(0, _rateLimiter.Calls);
    Assert.Empty(_sink.Calls);
  }

  // Paso 3: queda una ficha. Es el único rechazo que cuenta como falta.
  [Fact]
  public void Admit_NoTokens_RejectsWithRateLimitedAndCountsAsViolation()
  {
    _rateLimiter.HasTokens = false;

    var decision = CreateAdmitter().Admit(Message);

    AssertRejected(decision, NetworkErrors.RateLimited, isViolation: true);
    Assert.Equal(1, _rateLimiter.Calls);
    Assert.Empty(_sink.Calls);
  }

  // Paso 4: la cola global tiene lugar. La ficha ya se había gastado.
  [Fact]
  public void Admit_QueueFull_RejectsWithCommandQueueFullAndNoViolation()
  {
    _sink.Result = CommandAdmission.Rejected(CommonErrors.CommandQueueFull);

    var decision = CreateAdmitter().Admit(Message);

    AssertRejected(decision, CommonErrors.CommandQueueFull, isViolation: false);
    Assert.Equal(1, _rateLimiter.Calls);
    Assert.Single(_sink.Calls);
  }

  // El orden: cada paso se evalúa solo si los anteriores pasaron.
  [Fact]
  public void Admit_EverythingFails_ReportsTheFirstStep()
  {
    _gate.AcceptsGameplay = false;
    _gate.Player = null;
    _rateLimiter.HasTokens = false;
    _sink.Result = CommandAdmission.Rejected(CommonErrors.CommandQueueFull);

    var decision = CreateAdmitter().Admit(Message);

    AssertRejected(decision, CommonErrors.MatchNotRunning, isViolation: false);
  }

  [Fact]
  public void Admit_NoPlayerAndNoTokens_ReportsNotRegisteredAndDoesNotSpendAToken()
  {
    _gate.Player = null;
    _rateLimiter.HasTokens = false;

    var decision = CreateAdmitter().Admit(Message);

    AssertRejected(decision, NetworkErrors.NotRegistered, isViolation: false);
    Assert.Equal(0, _rateLimiter.Calls);
  }

  [Fact]
  public void Admit_NoTokensAndFullQueue_ReportsRateLimitedAndNeverTouchesTheQueue()
  {
    _rateLimiter.HasTokens = false;
    _sink.Result = CommandAdmission.Rejected(CommonErrors.CommandQueueFull);

    var decision = CreateAdmitter().Admit(Message);

    AssertRejected(decision, NetworkErrors.RateLimited, isViolation: true);
    Assert.Empty(_sink.Calls);
  }

  [Fact]
  public void Admit_RejectedCommand_CanBeRetriedAndIsAdmittedOnceThereIsRoom()
  {
    var admitter = CreateAdmitter();
    _sink.Result = CommandAdmission.Rejected(CommonErrors.CommandQueueFull);
    Assert.False(admitter.Admit(Message).IsAdmitted);

    _sink.Result = CommandAdmission.Admitted(new CommandSequence(5));

    Assert.True(admitter.Admit(Message).IsAdmitted);
  }

  // D-028: RepathResponse es un comando de gameplay más. Pasa por la cola global con su Sequence y gasta una ficha.
  private static readonly GameplayMessage RepathResponse = new(
      new ClientCommandId(10),
      new RepathResponseCommand(new EntityId(3), new OrderId(2), new MapRevision(1), EquatableArray<PositionXZ>.Empty));

  [Fact]
  public void Admit_RepathResponse_GoesThroughTheQueueAndSpendsAToken()
  {
    _sink.Result = CommandAdmission.Admitted(new CommandSequence(43));

    var decision = CreateAdmitter().Admit(RepathResponse);

    Assert.Equal(new CommandSequence(43), decision.Sequence);
    Assert.Equal(1, _rateLimiter.Calls);
    var call = Assert.Single(_sink.Calls);
    Assert.Equal((PlayerOfTheConnection, RepathResponse.ClientCommandId), (call.Player, call.ClientCommandId));
    Assert.Same(RepathResponse.Command, call.Command);
  }

  [Fact]
  public void Admit_RepathResponseWithoutTokens_IsRateLimitedLikeAnyOtherCommand()
  {
    _rateLimiter.HasTokens = false;

    var decision = CreateAdmitter().Admit(RepathResponse);

    AssertRejected(decision, NetworkErrors.RateLimited, isViolation: true);
    Assert.Empty(_sink.Calls);
  }

  [Fact]
  public void Admit_NullMessage_Throws()
  {
    Assert.Throws<ArgumentNullException>(() => CreateAdmitter().Admit(null!));
  }

  [Fact]
  public void Constructor_NullDependency_Throws()
  {
    Assert.Throws<ArgumentNullException>(() => new CommandAdmitter(Connection, null!, _rateLimiter, _sink));
    Assert.Throws<ArgumentNullException>(() => new CommandAdmitter(Connection, _gate, null!, _sink));
    Assert.Throws<ArgumentNullException>(() => new CommandAdmitter(Connection, _gate, _rateLimiter, null!));
  }

  private static void AssertRejected(AdmissionDecision decision, ErrorCode expectedError, bool isViolation)
  {
    Assert.False(decision.IsAdmitted);
    Assert.Equal(expectedError, decision.Error);
    Assert.Equal(isViolation, decision.IsViolation);
    Assert.Null(decision.Sequence);
  }
}
