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
  private readonly FakeViolationTracker _violations = new();

  private InboundRouter CreateRouter() =>
      new(Connection, ServerVersion, _session, new CommandAdmitter(Connection, _gate, _rateLimiter, _commands), _violations, _output);

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

  // ───────────── Faltas y abuso (08 §6, D-074) ─────────────

  public static TheoryData<string> ViolationScenarios => ["parser", "invalidCommand", "readyBeforeHello", "gameplayBeforeHello", "duplicateHello", "rateLimited"];

  public static TheoryData<string> NonViolationScenarios => ["matchNotRunning", "notRegistered", "commandQueueFull", "readySessionBusy", "unmatchedPong", "admitted"];

  [Theory]
  [MemberData(nameof(ViolationScenarios))]
  public void Violation_IsRegisteredWithItsErrorCode(string scenario)
  {
    ErrorCode expected = Play(scenario);

    Assert.Equal([expected], _violations.Registered);
    Assert.Empty(_output.Closes);
  }

  [Theory]
  [MemberData(nameof(NonViolationScenarios))]
  public void NotTheClientsFault_IsNotRegistered(string scenario)
  {
    Play(scenario);

    Assert.Empty(_violations.Registered);
  }

  [Theory]
  [MemberData(nameof(ViolationScenarios))]
  public void Violation_OverTheLimit_AnswersAndThenClosesWithAbuseDetected(string scenario)
  {
    _violations.LimitExceeded = true;

    Play(scenario);

    // El cliente recibe la respuesta a su mensaje y después el cierre, que encola el Disconnecting detrás.
    Assert.IsType<OutboundMessage>(_output.Actions[^2]);
    Assert.Equal(NetworkErrors.AbuseDetected, Assert.IsType<ErrorCode>(_output.Actions[^1]));
  }

  [Fact]
  public void UnsupportedVersion_OfHello_ClosesWithUnsupportedVersionEvenOverTheLimit()
  {
    _violations.LimitExceeded = true;
    var router = CreateRouter();

    router.Handle(ParseResult.Violation(NetworkErrors.UnsupportedVersion, null, "Versión 2 no soportada.", ProtocolMessageTypes.Hello));

    Assert.Equal([NetworkErrors.UnsupportedVersion], _output.Closes);
  }

  /// <summary>Prepara y ejecuta un caso; devuelve el código que el cliente debería ver como falta, si lo es.</summary>
  private ErrorCode Play(string scenario)
  {
    switch (scenario)
    {
      case "parser":
        CreateRouter().Handle(ParseResult.Violation(NetworkErrors.MalformedJson, null, "no es JSON"));
        return NetworkErrors.MalformedJson;
      case "invalidCommand":
        CreateAcceptedRouter().Handle(ParseResult.InvalidCommand(Gameplay.ClientCommandId, "estructura inválida"));
        return CommonErrors.InvalidCommand;
      case "readyBeforeHello":
        CreateRouter().Handle(Parsed(new ClientReadyMessage(new InitializationId(1))));
        return NetworkErrors.HelloRequired;
      case "gameplayBeforeHello":
        CreateRouter().Handle(Parsed(Gameplay));
        return NetworkErrors.HelloRequired;
      case "duplicateHello":
        CreateAcceptedRouter().Handle(Parsed(new HelloMessage(null)));
        return NetworkErrors.DuplicateHello;
      case "rateLimited":
        _rateLimiter.HasTokens = false;
        CreateAcceptedRouter().Handle(Parsed(Gameplay));
        return NetworkErrors.RateLimited;
      case "matchNotRunning":
        _gate.AcceptsGameplay = false;
        CreateAcceptedRouter().Handle(Parsed(Gameplay));
        return CommonErrors.MatchNotRunning;
      case "notRegistered":
        _gate.Player = null;
        CreateAcceptedRouter().Handle(Parsed(Gameplay));
        return NetworkErrors.NotRegistered;
      case "commandQueueFull":
        _commands.Result = CommandAdmission.Rejected(CommonErrors.CommandQueueFull);
        CreateAcceptedRouter().Handle(Parsed(Gameplay));
        return CommonErrors.CommandQueueFull;
      case "readySessionBusy":
        _session.AcceptReady = false;
        CreateAcceptedRouter().Handle(Parsed(new ClientReadyMessage(new InitializationId(1))));
        return NetworkErrors.SessionBusy;
      case "unmatchedPong":
        CreateAcceptedRouter().Handle(Parsed(new PongMessage(1)));
        return default;
      case "admitted":
        CreateAcceptedRouter().Handle(Parsed(Gameplay));
        return default;
      default:
        throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
    }
  }
}
