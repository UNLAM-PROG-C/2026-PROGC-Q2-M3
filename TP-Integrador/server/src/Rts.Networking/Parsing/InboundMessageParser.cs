using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Unicode;
using Rts.Protocol;
using Rts.Protocol.Commands;
using Rts.Protocol.Session;
using Rts.Protocol.Transport;
using Rts.Shared.Collections;
using Rts.Shared.Commands;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Networking.Parsing;

/// <summary>
/// Implementación de <see cref="IInboundMessageParser"/>. Puro, sin estado y seguro para hilos: lo único que guarda es la versión del
/// protocolo, que no cambia. Una línea mala nunca lanza una excepción: siempre devuelve un <see cref="ParseResult"/>.
/// </summary>
internal sealed class InboundMessageParser : IInboundMessageParser
{
  private readonly int _protocolVersion;

  public InboundMessageParser(int protocolVersion = NetworkEnvelope.InitialVersion)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(protocolVersion);
    _protocolVersion = protocolVersion;
  }

  /// <inheritdoc/>
  public ParseResult Parse(ReadOnlySpan<byte> frame)
  {
    // Control 1: UTF-8 válido.
    if (!Utf8.IsValid(frame))
    {
      return ParseResult.Violation(NetworkErrors.InvalidUtf8, null, "La línea no es UTF-8 válido.");
    }

    // Control 2: JSON bien formado. JsonDocument.Parse no acepta un Span, por eso la copia (la línea ya está acotada por el framing).
    JsonDocument document;
    try
    {
      document = JsonDocument.Parse(frame.ToArray());
    }
    catch (JsonException)
    {
      return ParseResult.Violation(NetworkErrors.MalformedJson, null, "La línea no es JSON válido.");
    }

    using (document)
    {
      return ParseEnvelope(document.RootElement);
    }
  }

  private ParseResult ParseEnvelope(JsonElement root)
  {
    // Control 2 (cont.): tiene que ser un objeto, no un arreglo, número, texto ni null.
    if (root.ValueKind != JsonValueKind.Object)
    {
      return ParseResult.Violation(NetworkErrors.MalformedJson, null, "La línea no es un objeto JSON.");
    }

    // Se lee primero para poder devolverlo en cualquier error que venga después (correlación).
    long? messageId = ReadMessageId(root);

    // Control 3: el sobre trae protocolVersion (entero) y type (texto no vacío).
    if (!TryReadInt32(root, "protocolVersion", out int version))
    {
      return ParseResult.Violation(NetworkErrors.InvalidEnvelope, messageId, "Falta protocolVersion o no es un entero.");
    }

    if (!TryReadString(root, "type", out string? type))
    {
      return ParseResult.Violation(NetworkErrors.InvalidEnvelope, messageId, "Falta type o no es un texto.");
    }

    // Control 4: la versión es la del servidor.
    if (version != _protocolVersion)
    {
      return ParseResult.Violation(
          NetworkErrors.UnsupportedVersion,
          messageId,
          $"Versión {version} no soportada; el servidor usa la {_protocolVersion}.");
    }

    // Control 5: el tipo es uno que el cliente puede enviar.
    if (!ProtocolMessageTypes.ClientToServer.Contains(type))
    {
      return ParseResult.Violation(NetworkErrors.UnknownType, messageId, $"El tipo '{type}' no existe para un cliente.");
    }

    JsonElement? payload = ReadPayload(root);

    return type switch
    {
      ProtocolMessageTypes.Hello => ParseHello(payload, messageId),
      ProtocolMessageTypes.ClientReady => ParseClientReady(payload, messageId),
      ProtocolMessageTypes.Ping => ParsePing(payload, messageId),
      ProtocolMessageTypes.Pong => ParsePong(payload, messageId),
      ProtocolMessageTypes.Move => ParseMove(payload, messageId),

      // Llegan acá los comandos de gameplay de la fase 2: se reconocen pero todavía no se convierten (decisión provisoria).
      _ => ParseResult.Violation(NetworkErrors.UnknownType, messageId, $"El tipo '{type}' todavía no está soportado."),
    };
  }

  /// <summary>El payload de <c>Hello</c> es opcional (el nombre también), pero si viene tiene que ser un objeto bien armado.</summary>
  private static ParseResult ParseHello(JsonElement? payload, long? messageId)
  {
    if (payload is null)
    {
      return ParseResult.Parsed(new HelloMessage(null));
    }

    if (!TryReadPayload(payload, out HelloDto? dto))
    {
      return InvalidPayload(messageId, "El payload de Hello no es un objeto válido.");
    }

    return ParseResult.Parsed(new HelloMessage(dto.ClientName));
  }

  private static ParseResult ParseClientReady(JsonElement? payload, long? messageId)
  {
    if (!TryReadPayload(payload, out ClientReadyDto? dto))
    {
      return InvalidPayload(messageId, "ClientReady requiere un payload con initializationId.");
    }

    return ParseResult.Parsed(new ClientReadyMessage(new InitializationId(dto.InitializationId)));
  }

  private static ParseResult ParsePing(JsonElement? payload, long? messageId)
  {
    if (!TryReadPayload(payload, out PingDto? dto))
    {
      return InvalidPayload(messageId, "Ping requiere un payload con pingId.");
    }

    return ParseResult.Parsed(new PingMessage(dto.PingId));
  }

  private static ParseResult ParsePong(JsonElement? payload, long? messageId)
  {
    if (!TryReadPayload(payload, out PongDto? dto))
    {
      return InvalidPayload(messageId, "Pong requiere un payload con pingId.");
    }

    return ParseResult.Parsed(new PongMessage(dto.PingId));
  }

  private static ParseResult InvalidPayload(long? messageId, string detail) =>
      ParseResult.Violation(NetworkErrors.InvalidPayload, messageId, detail);

  private static ParseResult ParseMove(JsonElement? payload, long? messageId)
  {
    // Control 6: en gameplay el messageId es obligatorio y es el ClientCommandId, en [1, 2^53] (D-005).
    if (messageId is not long id || !new ClientCommandId(id).IsInValidRange)
    {
      return ParseResult.Violation(NetworkErrors.InvalidMessageId, messageId, "Move requiere un messageId entero entre 1 y 2^53.");
    }

    var clientCommandId = new ClientCommandId(id);

    // Desde acá el messageId es válido: si el payload falla, el cliente recibe un CommandRejected con ese id (D-075).
    if (!TryReadPayload(payload, out MoveCommandDto? dto))
    {
      return ParseResult.InvalidCommand(clientCommandId, "El payload de Move no tiene la estructura esperada.");
    }

    if (!TryBuildMove(dto, out MoveCommand? command, out string detail))
    {
      return ParseResult.InvalidCommand(clientCommandId, detail);
    }

    return ParseResult.Parsed(new GameplayMessage(clientCommandId, command));
  }

  /// <summary>
  /// DTO → <see cref="MoveCommand"/>. Los campos que el deserializador deja en <c>null</c> se revisan a mano, y las
  /// <see cref="ArgumentException"/> de los constructores de dominio (coordenadas no finitas, sin rutas, unidad repetida o inválida)
  /// se traducen en un fallo en vez de dejarlas escapar.
  /// </summary>
  private static bool TryBuildMove(MoveCommandDto dto, [NotNullWhen(true)] out MoveCommand? command, out string detail)
  {
    command = null;

    if (dto.Destination is null
        || dto.Routes is null
        || dto.Routes.Any(route => route is null || route.Waypoints is null || route.Waypoints.Any(point => point is null)))
    {
      detail = "Move necesita destination y routes completos, sin elementos nulos.";
      return false;
    }

    try
    {
      EquatableArray<UnitRouteCandidate> routes = dto.Routes
          .Select(route => new UnitRouteCandidate(
              new EntityId(route.UnitId),
              route.Waypoints.Select(point => new PositionXZ(point.X, point.Z)).ToEquatableArray()))
          .ToEquatableArray();

      command = new MoveCommand(new PositionXZ(dto.Destination.X, dto.Destination.Z), new MapRevision(dto.RouteMapRevision), routes);
      detail = string.Empty;
      return true;
    }
    catch (ArgumentException exception)
    {
      detail = exception.Message;
      return false;
    }
  }

  /// <summary><c>messageId</c> si es un entero que cabe en 64 bits; no valida el rango (eso es solo para gameplay).</summary>
  private static long? ReadMessageId(JsonElement root) =>
      root.TryGetProperty("messageId", out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long id)
          ? id
          : null;

  /// <summary>El <c>payload</c> del sobre; un <c>null</c> explícito cuenta como ausente.</summary>
  private static JsonElement? ReadPayload(JsonElement root) =>
      root.TryGetProperty("payload", out JsonElement element) && element.ValueKind != JsonValueKind.Null
          ? element
          : null;

  private static bool TryReadInt32(JsonElement root, string name, out int value)
  {
    value = 0;
    return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
  }

  private static bool TryReadString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
  {
    value = null;
    if (root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String)
    {
      value = element.GetString();
    }

    return !string.IsNullOrEmpty(value);
  }

  /// <summary>Convierte el payload al DTO <typeparamref name="T"/>; falla si falta, no es un objeto o los tipos no coinciden.</summary>
  private static bool TryReadPayload<T>(JsonElement? payload, [NotNullWhen(true)] out T? dto)
      where T : class
  {
    dto = null;
    if (payload is null)
    {
      return false;
    }

    try
    {
      dto = payload.Value.Deserialize<T>(ProtocolJson.Options);
    }
    catch (JsonException)
    {
      return false;
    }

    return dto is not null;
  }
}
