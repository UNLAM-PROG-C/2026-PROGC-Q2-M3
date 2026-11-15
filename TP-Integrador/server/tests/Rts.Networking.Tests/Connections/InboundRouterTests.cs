using Rts.Networking.Admission;
using Rts.Networking.Connections;
using Rts.Networking.Parsing;
using Rts.Networking.Tests.Admission;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Transport;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests.Connections;

public class InboundRouterTests
{
  private const int ServerVersion = 1;

  private static readonly ConnectionId Connection = new(7);

  private static readonly GameplayMessage Gameplay = new(
      new ClientCommandId(9),
      new StopCommand(EquatableArray.Create<EntityId>([new EntityId(3)])));

  private readonly FakeSessionEventSink _session = new();
  private readonly FakeConnectionOutput _output = new();
  private readonly FakeSessionGate _gate = new() { Player = PlayerId.Two };
  private readonly FakeRateLimiter _rateLimiter = new();
  private readonly FakeCommandSink _commands = new();

  private InboundRouter CreateRouter() =>
      new(Connection, ServerVersion, _session, new CommandAdmitter(Connection, _gate, _rateLimiter, _commands), _output);

  private InboundRouter CreateAcceptedRouter()
  {
    var router = CreateRouter();
    router.Handle(ParseResult.Parsed(new HelloMessage(null)));
    return router;
  }

  private static ParseResult Parsed(InboundMessage message) => ParseResult.Parsed(message);

  private static T PayloadOf<T>(OutboundMessage message, string expectedType)
      where T : class, IOutboundPayload
  {
    Assert.Equal(expectedType, message.Type);
    Assert.Equal(DeliveryClass.Essential, message.Delivery);
    return Assert.IsType<T>(message.Payload);
  }

  // ───────────── Hello (08 §3) ─────────────

  [Fact]
  public void Hello_InAwaitingHello_IsPostedToTheSessionAndAcceptsTheConnection()
  {
    var router = CreateRouter();
    Assert.False(router.HelloAccepted);

    router.Handle(Parsed(new HelloMessage("godot")));

    Assert.Equal([Connection], _session.Hellos);
    Assert.True(router.HelloAccepted);
    Assert.Empty(_output.Actions);
  }

  [Fact]
  public void Hello_WhenTheSessionQueueIsFull_ClosesWithSessionBusy()
  {
    _session.AcceptHello = false;
    var router = CreateRouter();

    router.Handle(Parsed(new HelloMessage(null)));

    Assert.Equal([NetworkErrors.SessionBusy], _output.Closes);
    Assert.False(router.HelloAccepted);
  }

  [Fact]
  public void Hello_Twice_IsDuplicateHelloAndIsNotPostedAgain()
  {
    var router = CreateAcceptedRouter();

    router.Handle(Parsed(new HelloMessage(null)));

    Assert.Single(_session.Hellos);
    var error = PayloadOf<ProtocolErrorDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.ProtocolError);
    Assert.Equal(NetworkErrors.DuplicateHello.Value, error.Error);
    Assert.Empty(_output.Closes);
  }

  [Fact]
  public void ClientReady_BeforeHello_IsHelloRequiredAndIsNotPosted()
  {
    var router = CreateRouter();

    router.Handle(Parsed(new ClientReadyMessage(new InitializationId(1))));

    var error = PayloadOf<ProtocolErrorDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.ProtocolError);
    Assert.Equal(NetworkErrors.HelloRequired.Value, error.Error);
    Assert.Empty(_session.Readies);
  }

  [Fact]
  public void Gameplay_BeforeHello_IsHelloRequiredWithItsMessageIdAndIsNotAdmitted()
  {
    var router = CreateRouter();

    router.Handle(Parsed(Gameplay));

    var error = PayloadOf<ProtocolErrorDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.ProtocolError);
    Assert.Equal(NetworkErrors.HelloRequired.Value, error.Error);
    Assert.Equal(9, error.MessageId);
    Assert.Empty(_commands.Calls);
    Assert.Equal(0, _rateLimiter.Calls);
  }

  // ───────────── Ping, Pong y ClientReady (08 §4) ─────────────

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Ping_AnswersPongWithTheSamePingId_EvenBeforeHello(bool afterHello)
  {
    var router = afterHello ? CreateAcceptedRouter() : CreateRouter();

    router.Handle(Parsed(new PingMessage(42)));

    var pong = PayloadOf<PongDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.Pong);
    Assert.Equal(42, pong.PingId);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Pong_IsIgnoredWithoutAFault(bool afterHello)
  {
    var router = afterHello ? CreateAcceptedRouter() : CreateRouter();

    router.Handle(Parsed(new PongMessage(42)));

    Assert.Empty(_output.Actions);
  }

  [Fact]
  public void ClientReady_AfterHello_IsPostedToTheSession()
  {
    var router = CreateAcceptedRouter();

    router.Handle(Parsed(new ClientReadyMessage(new InitializationId(3))));

    Assert.Equal([(Connection, new InitializationId(3))], _session.Readies);
    Assert.Empty(_output.Actions);
  }

  [Fact]
  public void ClientReady_WhenTheSessionQueueIsFull_AnswersSessionBusyAndKeepsTheConnection()
  {
    _session.AcceptReady = false;
    var router = CreateAcceptedRouter();

    router.Handle(Parsed(new ClientReadyMessage(new InitializationId(3))));

    var error = PayloadOf<ProtocolErrorDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.ProtocolError);
    Assert.Equal(NetworkErrors.SessionBusy.Value, error.Error);
    Assert.Empty(_output.Closes);
  }

  // ───────────── Gameplay (08 §5) ─────────────

  [Fact]
  public void Gameplay_Admitted_SendsNothingAndTheSinkGetsThePlayerOfTheConnection()
  {
    var router = CreateAcceptedRouter();

    router.Handle(Parsed(Gameplay));

    Assert.Empty(_output.Actions);
    var call = Assert.Single(_commands.Calls);
    Assert.Equal(PlayerId.Two, call.Player);
    Assert.Equal(Gameplay.ClientCommandId, call.ClientCommandId);
  }

  [Fact]
  public void Gameplay_OutsideRunning_IsRejectedWithMatchNotRunning()
  {
    _gate.AcceptsGameplay = false;
    var router = CreateAcceptedRouter();

    router.Handle(Parsed(Gameplay));

    AssertRejected(CommonErrors.MatchNotRunning);
  }

  [Fact]
  public void Gameplay_OverTheRateLimit_IsRejectedWithRateLimited()
  {
    _rateLimiter.HasTokens = false;
    var router = CreateAcceptedRouter();

    router.Handle(Parsed(Gameplay));

    AssertRejected(NetworkErrors.RateLimited);
  }

  [Fact]
  public void Gameplay_WhenTheGlobalQueueIsFull_IsRejectedWithCommandQueueFull()
  {
    _commands.Result = CommandAdmission.Rejected(CommonErrors.CommandQueueFull);
    var router = CreateAcceptedRouter();

    router.Handle(Parsed(Gameplay));

    AssertRejected(CommonErrors.CommandQueueFull);
  }

  private void AssertRejected(ErrorCode expected)
  {
    var rejected = PayloadOf<CommandRejectedDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.CommandRejected);
    Assert.Equal(9, rejected.MessageId);
    Assert.Equal(expected.Value, rejected.Error);
    Assert.Empty(_output.Closes);
  }

  // ───────────── Fallos del parser (08 §4) ─────────────

  [Fact]
  public void ProtocolViolation_SendsProtocolErrorWithTheDetailAndKeepsTheConnection()
  {
    var router = CreateRouter();

    router.Handle(ParseResult.Violation(NetworkErrors.MalformedJson, 5, "La línea no es JSON válido."));

    var error = PayloadOf<ProtocolErrorDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.ProtocolError);
    Assert.Equal(NetworkErrors.MalformedJson.Value, error.Error);
    Assert.Equal(5, error.MessageId);
    Assert.Equal("La línea no es JSON válido.", error.Detail);
    Assert.Null(error.ServerProtocolVersion);
    Assert.Empty(_output.Closes);
  }

  [Fact]
  public void UnsupportedVersion_OfAnyOtherMessage_AnswersWithTheServerVersionAndKeepsTheConnection()
  {
    var router = CreateAcceptedRouter();

    router.Handle(ParseResult.Violation(NetworkErrors.UnsupportedVersion, null, "Versión 2 no soportada.", ProtocolMessageTypes.Ping));

    var error = PayloadOf<ProtocolErrorDto>(Assert.Single(_output.Sent), ProtocolMessageTypes.ProtocolError);
    Assert.Equal(NetworkErrors.UnsupportedVersion.Value, error.Error);
    Assert.Equal(ServerVersion, error.ServerProtocolVersion);
    Assert.Empty(_output.Closes);
  }

  [Fact]
  public void UnsupportedVersion_OfHello_AnswersAndThenClosesWithUnsupportedVersion()
  {
    var router = CreateRouter();

    router.Handle(ParseResult.Violation(NetworkErrors.UnsupportedVersion, null, "Versión 2 no soportada.", ProtocolMessageTypes.Hello));

    // Primero el ProtocolError y después el cierre, que encola el Disconnecting detrás de él.
    Assert.Equal(2, _output.Actions.Count);
    var error = PayloadOf<ProtocolErrorDto>(Assert.IsType<OutboundMessage>(_output.Actions[0]), ProtocolMessageTypes.ProtocolError);
    Assert.Equal(ServerVersion, error.ServerProtocolVersion);
    Assert.Equal(NetworkErrors.UnsupportedVersion, Assert.IsType<ErrorCode>(_output.Actions[1]));
    Assert.Empty(_session.Hellos);
  }

  [Fact]
  public void InvalidCommand_IsRejectedWithItsMessageId()
  {
    var router = CreateAcceptedRouter();

    router.Handle(ParseResult.InvalidCommand(new ClientCommandId(9), "estructura inválida"));

    AssertRejected(CommonErrors.InvalidCommand);
    Assert.Empty(_commands.Calls);
  }
}
