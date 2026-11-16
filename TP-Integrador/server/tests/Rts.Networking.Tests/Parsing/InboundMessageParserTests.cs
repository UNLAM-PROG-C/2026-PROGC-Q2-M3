using System.Text;
using Rts.Networking.Parsing;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests.Parsing;

public class InboundMessageParserTests
{
  [Fact]
  public void Parse_ValidPing_ReturnsPingMessage()
  {
    var parser = new InboundMessageParser();
    var frame = Encoding.UTF8.GetBytes("""{"protocolVersion":1,"type":"Ping","payload":{"pingId":7}}""");

    var result = parser.Parse(frame);

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(new PingMessage(7), result.Message);
  }

  [Fact]
  public void Parse_InvalidUtf8_ReturnsInvalidUtf8Violation()
  {
    var parser = new InboundMessageParser();
    byte[] frame = [0xFF, 0xFE, 0xFD];

    var result = parser.Parse(frame);

    Assert.Equal(ParseOutcome.ProtocolViolation, result.Outcome);
    Assert.Equal(NetworkErrors.InvalidUtf8, result.Error);
  }

  // Control 2: la línea tiene que ser un objeto JSON.
  [Theory]
  [InlineData("hola")]
  [InlineData("{\"protocolVersion\":1")]
  [InlineData("")]
  [InlineData("[1,2]")]
  [InlineData("42")]
  [InlineData("\"texto\"")]
  [InlineData("null")]
  public void Parse_NotAJsonObject_ReturnsMalformedJsonViolation(string line)
  {
    var result = ParseLine(line);

    AssertViolation(result, NetworkErrors.MalformedJson);
  }

  // Control 3: el sobre necesita protocolVersion (entero) y type (texto no vacío).
  [Theory]
  [InlineData("""{"type":"Ping","payload":{"pingId":7}}""")]
  [InlineData("""{"protocolVersion":1,"payload":{"pingId":7}}""")]
  [InlineData("""{"protocolVersion":"1","type":"Ping","payload":{"pingId":7}}""")]
  [InlineData("""{"protocolVersion":1.5,"type":"Ping","payload":{"pingId":7}}""")]
  [InlineData("""{"protocolVersion":1,"type":5,"payload":{"pingId":7}}""")]
  [InlineData("""{"protocolVersion":1,"type":"","payload":{"pingId":7}}""")]
  public void Parse_IncompleteEnvelope_ReturnsInvalidEnvelopeViolation(string line)
  {
    var result = ParseLine(line);

    AssertViolation(result, NetworkErrors.InvalidEnvelope);
  }

  // Control 4: la versión tiene que ser la del servidor.
  [Fact]
  public void Parse_DifferentVersion_ReturnsUnsupportedVersionViolation()
  {
    var result = ParseLine("""{"protocolVersion":2,"type":"Ping","payload":{"pingId":7}}""");

    AssertViolation(result, NetworkErrors.UnsupportedVersion);
  }

  [Theory]
  [InlineData("Hello")]
  [InlineData("Ping")]
  public void Parse_DifferentVersion_CarriesTheMessageType(string type)
  {
    // 08 §3: un Hello con otra versión cierra la conexión; cualquier otro mensaje con otra versión es solo una falta.
    var result = ParseLine("{\"protocolVersion\":2,\"type\":\"" + type + "\"}");

    Assert.Equal(type, result.MessageType);
  }

  [Fact]
  public void Parse_ServerVersionFromConstructor_IsTheOneAccepted()
  {
    var parser = new InboundMessageParser(2);

    var accepted = parser.Parse(Bytes("""{"protocolVersion":2,"type":"Ping","payload":{"pingId":7}}"""));
    var rejected = parser.Parse(Bytes("""{"protocolVersion":1,"type":"Ping","payload":{"pingId":7}}"""));

    Assert.Equal(ParseOutcome.Parsed, accepted.Outcome);
    AssertViolation(rejected, NetworkErrors.UnsupportedVersion);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Constructor_NonPositiveVersion_Throws(int version)
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new InboundMessageParser(version));
  }

  // Control 5: el tipo tiene que ser uno que el cliente pueda enviar.
  [Theory]
  [InlineData("Dance")]
  [InlineData("MatchStarted")]
  [InlineData("ping")]
  public void Parse_TypeTheClientCannotSend_ReturnsUnknownTypeViolation(string type)
  {
    var result = ParseLine($$"""{"protocolVersion":1,"type":"{{type}}"}""");

    AssertViolation(result, NetworkErrors.UnknownType);
  }

  // Decisión provisoria de Flor (opción A): los comandos de la fase 2 todavía no se convierten.
  [Theory]
  [InlineData("Attack")]
  [InlineData("GatherWood")]
  [InlineData("Build")]
  [InlineData("RepairFarm")]
  [InlineData("TrainUnit")]
  [InlineData("RepathResponse")]
  public void Parse_GameplayTypeNotYetSupported_ReturnsUnknownTypeViolation(string type)
  {
    var result = ParseLine($$"""{"protocolVersion":1,"type":"{{type}}","payload":{},"messageId":3}""");

    AssertViolation(result, NetworkErrors.UnknownType);
    Assert.Contains("todavía no", result.Detail);
  }

  // El messageId se devuelve en el error cuando se pudo leer, para que el cliente correlacione.
  [Fact]
  public void Parse_ViolationAfterEnvelope_EchoesTheMessageId()
  {
    var result = ParseLine("""{"protocolVersion":9,"type":"Ping","messageId":42}""");

    AssertViolation(result, NetworkErrors.UnsupportedVersion);
    Assert.Equal(42, result.MessageId);
  }

  // Mensajes simples: Pong, Hello, ClientReady.
  [Fact]
  public void Parse_ValidPong_ReturnsPongMessage()
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"Pong","payload":{"pingId":7}}""");

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(new PongMessage(7), result.Message);
  }

  [Fact]
  public void Parse_HelloWithName_ReturnsHelloMessage()
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"Hello","payload":{"clientName":"Flor"}}""");

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(new HelloMessage("Flor"), result.Message);
  }

  [Theory]
  [InlineData("""{"protocolVersion":1,"type":"Hello"}""")]
  [InlineData("""{"protocolVersion":1,"type":"Hello","payload":null}""")]
  [InlineData("""{"protocolVersion":1,"type":"Hello","payload":{}}""")]
  public void Parse_HelloWithoutName_ReturnsHelloMessageWithNullName(string line)
  {
    var result = ParseLine(line);

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(new HelloMessage(null), result.Message);
  }

  [Fact]
  public void Parse_ValidClientReady_ReturnsClientReadyMessage()
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"ClientReady","payload":{"initializationId":3}}""");

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(new ClientReadyMessage(new InitializationId(3)), result.Message);
  }

  [Theory]
  [InlineData("""{"protocolVersion":1,"type":"Ping"}""")]
  [InlineData("""{"protocolVersion":1,"type":"Ping","payload":null}""")]
  [InlineData("""{"protocolVersion":1,"type":"Ping","payload":"siete"}""")]
  [InlineData("""{"protocolVersion":1,"type":"Ping","payload":{"pingId":"siete"}}""")]
  [InlineData("""{"protocolVersion":1,"type":"Pong"}""")]
  [InlineData("""{"protocolVersion":1,"type":"Pong","payload":[7]}""")]
  [InlineData("""{"protocolVersion":1,"type":"ClientReady"}""")]
  [InlineData("""{"protocolVersion":1,"type":"ClientReady","payload":{"initializationId":"uno"}}""")]
  [InlineData("""{"protocolVersion":1,"type":"Hello","payload":5}""")]
  [InlineData("""{"protocolVersion":1,"type":"Hello","payload":{"clientName":12}}""")]
  public void Parse_BadPayloadOfNonGameplayMessage_ReturnsInvalidPayloadViolation(string line)
  {
    var result = ParseLine(line);

    AssertViolation(result, NetworkErrors.InvalidPayload);
  }

  // Move: el único comando de gameplay de la fase 1.
  private const string ValidMove =
      """{"protocolVersion":1,"type":"Move","messageId":1,"payload":{"destination":{"x":10.5,"z":6.5},"routeMapRevision":0,"routes":[{"unitId":3,"waypoints":[{"x":9,"z":5},{"x":10.5,"z":6.5}]}]}}""";

  private static GameplayMessage ExpectedMove(long clientCommandId = 1) => new(
      new ClientCommandId(clientCommandId),
      new MoveCommand(
          new PositionXZ(10.5, 6.5),
          new MapRevision(0),
          EquatableArray.Create<UnitRouteCandidate>(
              [new UnitRouteCandidate(new EntityId(3), EquatableArray.Create<PositionXZ>([new PositionXZ(9, 5), new PositionXZ(10.5, 6.5)]))])));

  [Fact]
  public void Parse_ValidMove_ReturnsGameplayMessageWithMoveCommand()
  {
    var result = ParseLine(ValidMove);

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(ExpectedMove(), result.Message);
  }

  [Fact]
  public void Parse_MoveWithMaximumMessageId_IsAccepted()
  {
    var result = ParseLine(ValidMove.Replace("\"messageId\":1", "\"messageId\":9007199254740992"));

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(ExpectedMove(ClientCommandId.MaxValue), result.Message);
  }

  [Theory]
  [InlineData("")]
  [InlineData(",\"messageId\":0")]
  [InlineData(",\"messageId\":-5")]
  [InlineData(",\"messageId\":9007199254740993")]
  [InlineData(",\"messageId\":\"uno\"")]
  [InlineData(",\"messageId\":1.5")]
  [InlineData(",\"messageId\":null")]
  public void Parse_MoveWithoutValidMessageId_ReturnsInvalidMessageIdViolation(string messageIdProperty)
  {
    string line = "{\"protocolVersion\":1,\"type\":\"Move\"" + messageIdProperty
        + ",\"payload\":{\"destination\":{\"x\":1,\"z\":2},\"routeMapRevision\":0,\"routes\":[]}}";

    var result = ParseLine(line);

    AssertViolation(result, NetworkErrors.InvalidMessageId);
  }

  // Con messageId válido pero el payload mal armado: no es una falta de protocolo, es un comando inválido (D-075).
  [Theory]
  [InlineData("null")]
  [InlineData("\"mover\"")]
  [InlineData("{}")]
  [InlineData("""{"destination":{"x":1,"z":2},"routeMapRevision":0}""")]
  [InlineData("""{"destination":{"x":1,"z":2},"routeMapRevision":0,"routes":"x"}""")]
  [InlineData("""{"destination":{"x":1,"z":2},"routeMapRevision":0,"routes":[null]}""")]
  [InlineData("""{"destination":{"x":1,"z":2},"routeMapRevision":0,"routes":[{"unitId":3}]}""")]
  [InlineData("""{"destination":{"x":1,"z":2},"routeMapRevision":0,"routes":[{"unitId":3,"waypoints":[null]}]}""")]
  [InlineData("""{"destination":{"x":"uno","z":2},"routeMapRevision":0,"routes":[]}""")]
  [InlineData("""{"destination":{"x":1e999,"z":2},"routeMapRevision":0,"routes":[{"unitId":3,"waypoints":[]}]}""")]
  public void Parse_MoveWithMalformedPayload_ReturnsInvalidCommandWithItsClientCommandId(string payload)
  {
    string line = """{"protocolVersion":1,"type":"Move","messageId":5,"payload":""" + payload + "}";

    var result = ParseLine(line);

    AssertInvalidCommand(result, 5);
  }

  [Fact]
  public void Parse_MoveWithoutPayload_ReturnsInvalidCommandWithItsClientCommandId()
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"Move","messageId":5}""");

    AssertInvalidCommand(result, 5);
  }

  // Estructura correcta, pero el constructor de MoveCommand / UnitRouteCandidate se queja.
  [Theory]
  [InlineData("[]")]
  [InlineData("""[{"unitId":0,"waypoints":[]}]""")]
  [InlineData("""[{"unitId":3,"waypoints":[]},{"unitId":3,"waypoints":[]}]""")]
  public void Parse_MoveWhoseConstructorRejectsIt_ReturnsInvalidCommand(string routes)
  {
    string line = """{"protocolVersion":1,"type":"Move","messageId":8,"payload":{"destination":{"x":1,"z":2},"routeMapRevision":0,"routes":"""
        + routes + "}}";

    var result = ParseLine(line);

    AssertInvalidCommand(result, 8);
  }

  // Stop: solo la lista de unidades.
  [Fact]
  public void Parse_ValidStop_ReturnsGameplayMessageWithStopCommand()
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"Stop","messageId":4,"payload":{"unitIds":[3,8]}}""");

    Assert.Equal(ParseOutcome.Parsed, result.Outcome);
    Assert.Equal(
        new GameplayMessage(new ClientCommandId(4), new StopCommand(EquatableArray.Create<EntityId>([new EntityId(3), new EntityId(8)]))),
        result.Message);
  }

  [Fact]
  public void Parse_StopWithoutMessageId_ReturnsInvalidMessageIdViolation()
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"Stop","payload":{"unitIds":[3]}}""");

    AssertViolation(result, NetworkErrors.InvalidMessageId);
  }

  [Theory]
  [InlineData("")]
  [InlineData(""","payload":null""")]
  [InlineData(""","payload":{}""")]
  [InlineData(""","payload":{"unitIds":"x"}""")]
  [InlineData(""","payload":{"unitIds":[null]}""")]
  [InlineData(""","payload":{"unitIds":["tres"]}""")]
  public void Parse_StopWithMalformedPayload_ReturnsInvalidCommandWithItsClientCommandId(string payloadProperty)
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"Stop","messageId":6""" + payloadProperty + "}");

    AssertInvalidCommand(result, 6);
  }

  // Estructura correcta, pero el constructor de StopCommand se queja: sin unidades, una inválida o una repetida.
  [Theory]
  [InlineData("[]")]
  [InlineData("[0]")]
  [InlineData("[3,3]")]
  public void Parse_StopWhoseConstructorRejectsIt_ReturnsInvalidCommand(string unitIds)
  {
    var result = ParseLine("""{"protocolVersion":1,"type":"Stop","messageId":6,"payload":{"unitIds":""" + unitIds + "}}");

    AssertInvalidCommand(result, 6);
  }

  // El playerId nunca viene del cliente: si lo manda, se ignora.
  [Fact]
  public void Parse_MoveWithPlayerIdFields_IgnoresThem()
  {
    string line = ValidMove
        .Replace("\"messageId\":1,", "\"messageId\":1,\"playerId\":99,")
        .Replace("\"routeMapRevision\":0,", "\"routeMapRevision\":0,\"playerId\":99,");

    var result = ParseLine(line);

    Assert.Equal(ExpectedMove(), result.Message);
  }

  // Ninguna línea mala hace explotar al parser: todos los prefijos de un Move válido son inválidos y ninguno lanza.
  [Fact]
  public void Parse_EveryTruncationOfAValidMove_NeverThrowsAndNeverParses()
  {
    byte[] bytes = Bytes(ValidMove);
    var parser = new InboundMessageParser();

    for (int length = 0; length < bytes.Length; length++)
    {
      var result = parser.Parse(bytes.AsSpan(0, length));

      Assert.True(result.IsViolation, $"El prefijo de {length} bytes no debería ser válido.");
    }
  }

  private static void AssertInvalidCommand(ParseResult result, long expectedClientCommandId)
  {
    Assert.Equal(ParseOutcome.InvalidCommand, result.Outcome);
    Assert.Equal(CommonErrors.InvalidCommand, result.Error);
    Assert.Equal(new ClientCommandId(expectedClientCommandId), result.ClientCommandId);
    Assert.Equal(expectedClientCommandId, result.MessageId);
    Assert.True(result.IsViolation);
  }

  private static byte[] Bytes(string line) => Encoding.UTF8.GetBytes(line);

  private static ParseResult ParseLine(string line) => new InboundMessageParser().Parse(Bytes(line));

  private static void AssertViolation(ParseResult result, ErrorCode expectedError)
  {
    Assert.Equal(ParseOutcome.ProtocolViolation, result.Outcome);
    Assert.Equal(expectedError, result.Error);
    Assert.True(result.IsViolation);
  }
}
