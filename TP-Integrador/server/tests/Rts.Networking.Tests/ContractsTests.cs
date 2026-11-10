using Rts.Networking.Configuration;
using Rts.Networking.Framing;
using Rts.Networking.Parsing;
using Rts.Protocol;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests;

public class ContractsTests
{
  /// <summary>Configuración válida SOLO para pruebas; los números son suposiciones temporales.</summary>
  internal static NetworkingConfig ValidConfig() => new()
  {
    ListenAddress = "127.0.0.1",
    Port = 7777,
    MaxInboundMessageBytes = 64 * 1024,
    MaxPendingConnections = 4,
    HandshakeTimeout = TimeSpan.FromSeconds(5),
    CommandsPerSecond = 20,
    CommandBurst = 40,
    OutboundQueueCapacity = 256,
    MaxViolations = 10,
    ViolationWindow = TimeSpan.FromSeconds(10),
    PingIdleInterval = TimeSpan.FromSeconds(3),
    PongTimeout = TimeSpan.FromSeconds(5),
    CloseLinger = TimeSpan.FromMilliseconds(500),
    DrainTimeout = TimeSpan.FromSeconds(2),
  };

  [Fact]
  public void Config_ValidExampleUsesInitialProtocolVersion()
  {
    NetworkingConfig config = ValidConfig();

    Assert.Empty(config.GetValidationErrors());
    Assert.Equal(NetworkEnvelope.InitialVersion, config.ProtocolVersion);
  }

  [Fact]
  public void Config_ReportsEveryProblem()
  {
    NetworkingConfig config = ValidConfig() with
    {
      ListenAddress = "no-es-ip",
      Port = 0,
      CommandBurst = 5,
      OutboundQueueCapacity = 1,
      PongTimeout = TimeSpan.Zero,
    };

    Assert.Equal(5, config.GetValidationErrors().Count);
    Assert.Throws<ArgumentException>(config.Validate);
  }

  [Fact]
  public void ParseResult_EnforcesItsShape()
  {
    ParseResult parsed = ParseResult.Parsed(new PingMessage(3));
    ParseResult violation = ParseResult.Violation(NetworkErrors.MalformedJson, null, "llave sin cerrar");
    ParseResult invalidCommand = ParseResult.InvalidCommand(new ClientCommandId(9), "Build mezcla colocar y continuar");

    Assert.False(parsed.IsViolation);
    Assert.True(violation.IsViolation);
    Assert.Equal(CommonErrors.InvalidCommand, invalidCommand.Error);
    Assert.Equal(9, invalidCommand.MessageId);
    Assert.Throws<ArgumentException>(() => ParseResult.Violation(default, null, "sin código"));
    Assert.Throws<ArgumentException>(() => ParseResult.InvalidCommand(new ClientCommandId(0), "fuera de rango"));
  }

  [Fact]
  public void FrameResults_HaveNoDataUnlessFrame()
  {
    Assert.True(FrameReadResult.NeedMoreData.Frame.IsEmpty);
    Assert.Equal(FrameStatus.Oversize, FrameReadResult.Oversize.Status);
  }

  [Fact]
  public void MessageTypes_GameplayCoversEveryCommandType()
  {
    Assert.Equal(Enum.GetValues<CommandType>().Order(), ProtocolMessageTypes.GameplayTypes.Values.Order());
    IReadOnlySet<string> clientTypes = ProtocolMessageTypes.ClientToServer;
    Assert.Contains(ProtocolMessageTypes.Hello, clientTypes);
    Assert.DoesNotContain(ProtocolMessageTypes.StateEvents, clientTypes);
  }

  [Fact]
  public void Errors_BelongToNetworkArea()
  {
    Assert.All(
        [NetworkErrors.MalformedJson, NetworkErrors.UnsupportedVersion, NetworkErrors.RateLimited, NetworkErrors.MessageTooLarge,
             NetworkErrors.AbuseDetected, NetworkErrors.OutboundSaturated, NetworkErrors.HeartbeatTimeout],
        code => Assert.Equal("Network", code.Area));
  }
}
