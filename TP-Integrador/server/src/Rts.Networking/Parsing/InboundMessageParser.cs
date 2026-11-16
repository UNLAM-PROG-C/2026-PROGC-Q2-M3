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
          $"Versión {version} no soportada; el servidor usa la {_protocolVersion}.",
          type);
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
      ProtocolMessageTypes.Move => ParseGameplay<MoveCommandDto>(type, payload, messageId, BuildMove),
      ProtocolMessageTypes.Stop => ParseGameplay<StopCommandDto>(type, payload, messageId, BuildStop),
      ProtocolMessageTypes.Attack => ParseGameplay<AttackCommandDto>(type, payload, messageId,
          dto => new AttackCommand(new EntityId(dto.TargetId), new MapRevision(dto.RouteMapRevision), ReadRoutes(type, dto.Routes))),
      ProtocolMessageTypes.GatherWood => ParseGameplay<GatherWoodCommandDto>(type, payload, messageId,
          dto => new GatherWoodCommand(new EntityId(dto.TreeId), new MapRevision(dto.RouteMapRevision), ReadRoutes(type, dto.Routes))),
      ProtocolMessageTypes.Build => ParseGameplay<BuildCommandDto>(type, payload, messageId, BuildBuild),
      ProtocolMessageTypes.RepairFarm => ParseGameplay<RepairFarmCommandDto>(type, payload, messageId,
          dto => new RepairFarmCommand(new EntityId(dto.FarmId), new MapRevision(dto.RouteMapRevision), ReadRoutes(type, dto.Routes))),

      // Un tipo de unidad desconocido no llega al constructor: el deserializador ya lo rechaza (enums solo como texto).
      ProtocolMessageTypes.TrainUnit => ParseGameplay<TrainUnitCommandDto>(type, payload, messageId,
          dto => new TrainUnitCommand(new EntityId(dto.ProducerId), dto.UnitType)),

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

  /// <summary>
  /// Los controles comunes a todo comando de gameplay. Después, <paramref name="build"/> convierte el DTO en el comando de dominio:
  /// si un campo obligatorio vino en <c>null</c>, o el constructor de dominio rechaza los valores, lanza
  /// <see cref="ArgumentException"/>, que acá se traduce en un comando inválido en vez de dejarla escapar.
  /// </summary>
  private static ParseResult ParseGameplay<TDto>(string type, JsonElement? payload, long? messageId, Func<TDto, IGameCommand> build)
      where TDto : class
  {
    // Control 6: en gameplay el messageId es obligatorio y es el ClientCommandId, en [1, 2^53] (D-005).
    if (messageId is not long id || !new ClientCommandId(id).IsInValidRange)
    {
      return ParseResult.Violation(
          NetworkErrors.InvalidMessageId,
          messageId,
          $"{type} requiere un messageId entero entre 1 y {ClientCommandId.MaxValue}.");
    }

    var clientCommandId = new ClientCommandId(id);

    // Desde acá el messageId es válido: si el payload falla, el cliente recibe un CommandRejected con ese id (D-075).
    if (!TryReadPayload(payload, out TDto? dto))
    {
      return ParseResult.InvalidCommand(clientCommandId, $"El payload de {type} no tiene la estructura esperada.");
    }

    try
    {
      return ParseResult.Parsed(new GameplayMessage(clientCommandId, build(dto)));
    }
    catch (ArgumentException exception)
    {
      return ParseResult.InvalidCommand(clientCommandId, exception.Message);
    }
  }

  /// <summary>DTO → <see cref="MoveCommand"/>. El constructor rechaza coordenadas no finitas.</summary>
  private static MoveCommand BuildMove(MoveCommandDto dto)
  {
    if (dto.Destination is null)
    {
      throw new ArgumentException("Move necesita destination.");
    }

    return new MoveCommand(
        new PositionXZ(dto.Destination.X, dto.Destination.Z), new MapRevision(dto.RouteMapRevision), ReadRoutes(ProtocolMessageTypes.Move, dto.Routes));
  }

  /// <summary>
  /// Las rutas candidatas de un comando de unidades (Move, Attack, GatherWood, Build, RepairFarm). Los nulos que deja el
  /// deserializador se rechazan acá; las rutas vacías y las unidades repetidas o inválidas, en los constructores de dominio.
  /// </summary>
  private static EquatableArray<UnitRouteCandidate> ReadRoutes(string type, IReadOnlyList<UnitRouteDto>? routes)
  {
    if (routes is null || routes.Any(route => route is null || route.Waypoints is null || route.Waypoints.Any(point => point is null)))
    {
      throw new ArgumentException($"{type} necesita routes completas, sin elementos nulos.");
    }

    return routes
        .Select(route => new UnitRouteCandidate(
            new EntityId(route.UnitId),
            route.Waypoints.Select(point => new PositionXZ(point.X, point.Z)).ToEquatableArray()))
        .ToEquatableArray();
  }

  /// <summary>
  /// DTO → <see cref="BuildCommand"/>, en una de sus dos formas (D-032): colocar (tipo, columna y fila) o continuar una obra
  /// (<c>buildingId</c>). Mezclarlas, o no completar ninguna, es un comando inválido (08 §4).
  /// </summary>
  private static BuildCommand BuildBuild(BuildCommandDto dto)
  {
    bool anyPlacementField = dto.BuildingType is not null || dto.AnchorColumn is not null || dto.AnchorRow is not null;
    EquatableArray<UnitRouteCandidate> routes = ReadRoutes(ProtocolMessageTypes.Build, dto.Routes);
    var revision = new MapRevision(dto.RouteMapRevision);

    if (dto.BuildingId is { } buildingId && !anyPlacementField)
    {
      return BuildCommand.Continue(new EntityId(buildingId), revision, routes);
    }

    if (dto.BuildingId is null && dto is { BuildingType: { } buildingType, AnchorColumn: { } column, AnchorRow: { } row })
    {
      return BuildCommand.Place(buildingType, new GridCell(column, row), revision, routes);
    }

    throw new ArgumentException("Build lleva buildingType, anchorColumn y anchorRow para colocar, o solo buildingId para continuar.");
  }

  /// <summary>DTO → <see cref="StopCommand"/>. El constructor rechaza la lista vacía y las unidades inválidas o repetidas.</summary>
  private static StopCommand BuildStop(StopCommandDto dto)
  {
    if (dto.UnitIds is null)
    {
      throw new ArgumentException("Stop necesita unitIds.");
    }

    return new StopCommand(dto.UnitIds.Select(id => new EntityId(id)).ToEquatableArray());
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
