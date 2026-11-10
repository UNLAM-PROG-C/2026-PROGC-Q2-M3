using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Networking.Parsing;

/// <summary>Mensaje entrante ya validado estructuralmente y convertido a tipos de dominio (08 §2, §4).</summary>
public abstract record InboundMessage;

public sealed record HelloMessage(string? ClientName) : InboundMessage;

public sealed record ClientReadyMessage(InitializationId Initialization) : InboundMessage;

public sealed record PingMessage(long PingId) : InboundMessage;

public sealed record PongMessage(long PingId) : InboundMessage;

/// <summary>Comando de gameplay listo para Command Admission. No trae PlayerId: lo agrega el receptor desde la conexión (08 §3).</summary>
public sealed record GameplayMessage(ClientCommandId ClientCommandId, IGameCommand Command) : InboundMessage;

/// <summary>Clase de resultado de <see cref="IInboundMessageParser.Parse"/>.</summary>
public enum ParseOutcome
{
  /// <summary>Mensaje válido en <see cref="ParseResult.Message"/>.</summary>
  Parsed = 1,

  /// <summary>Mensaje inválido (UTF-8, JSON, sobre, versión, tipo, messageId o payload no gameplay): <c>ProtocolError</c>.</summary>
  ProtocolViolation = 2,

  /// <summary>
  /// Gameplay con <c>messageId</c> válido pero payload con estructura inválida: <c>CommandRejected(Common.InvalidCommand)</c> (D-075).
  /// </summary>
  InvalidCommand = 3,
}

/// <summary>
/// Resultado del parser. Ambos fallos cuentan como falta (D-074). Invariantes que verifica el constructor:
/// <c>Parsed</c> tiene mensaje y no error; los fallos tienen error y no mensaje; <c>InvalidCommand</c> tiene <see cref="ClientCommandId"/>.
/// </summary>
public sealed record ParseResult
{
  private ParseResult(ParseOutcome outcome, InboundMessage? message, ErrorCode? error, long? messageId, ClientCommandId? clientCommandId, string? detail)
  {
    Outcome = outcome;
    Message = message;
    Error = error;
    MessageId = messageId;
    ClientCommandId = clientCommandId;
    Detail = detail;
  }

  public ParseOutcome Outcome { get; }

  public InboundMessage? Message { get; }

  public ErrorCode? Error { get; }

  /// <summary><c>messageId</c> del sobre, si se pudo leer; se devuelve en el <c>ProtocolError</c> para correlación.</summary>
  public long? MessageId { get; }

  public ClientCommandId? ClientCommandId { get; }

  /// <summary>Explicación breve para el cliente y los logs.</summary>
  public string? Detail { get; }

  /// <summary>El mensaje cuenta como falta.</summary>
  public bool IsViolation => Outcome != ParseOutcome.Parsed;

  public static ParseResult Parsed(InboundMessage message)
  {
    ArgumentNullException.ThrowIfNull(message);
    return new ParseResult(ParseOutcome.Parsed, message, null, null, null, null);
  }

  public static ParseResult Violation(ErrorCode error, long? messageId, string detail)
  {
    RequireError(error);
    return new ParseResult(ParseOutcome.ProtocolViolation, null, error, messageId, null, detail);
  }

  public static ParseResult InvalidCommand(ClientCommandId clientCommandId, string detail)
  {
    if (!clientCommandId.IsInValidRange)
    {
      throw new ArgumentException($"ClientCommandId fuera de rango: {clientCommandId}.", nameof(clientCommandId));
    }

    return new ParseResult(ParseOutcome.InvalidCommand, null, CommonErrors.InvalidCommand, clientCommandId.Value, clientCommandId, detail);
  }

  private static void RequireError(ErrorCode error)
  {
    if (error.IsDefault)
    {
      throw new ArgumentException("Se requiere un código de error.", nameof(error));
    }
  }
}

/// <summary>
/// Parser estructural (08 §2 paso 3-4): UTF-8 → JSON → sobre → versión → tipo → <c>messageId</c> → payload → tipo de dominio.
/// <b>Puro, sin estado y seguro para hilos</b>: no conoce la fase de la conexión, la sesión ni reglas de gameplay. Las excepciones
/// de validación de los constructores de comandos se traducen en <see cref="ParseOutcome.InvalidCommand"/>.
/// </summary>
public interface IInboundMessageParser
{
  ParseResult Parse(ReadOnlySpan<byte> frame);
}
