using Rts.Networking.Admission;
using Rts.Networking.Parsing;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Transport;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking.Connections;

/// <summary>
/// Decide qué hacer con cada mensaje que llega por una conexión (contrato 08 §3 y §4), ya parseado. Hay uno por conexión y lo usa
/// solo su receptor.
/// <list type="bullet">
/// <item><b>Hello:</b> se informa a GameSession con <see cref="ISessionEventSink.TryPostHello"/>; si su cola está llena, se cierra con
/// <c>SessionBusy</c>. Un segundo <c>Hello</c> es <c>DuplicateHello</c>.</item>
/// <item><b>Antes del Hello</b> solo se aceptan <c>Hello</c>, <c>Ping</c> y <c>Pong</c>; cualquier otro mensaje recibe
/// <c>ProtocolError(HelloRequired)</c>.</item>
/// <item><b>ClientReady:</b> va a GameSession; si su cola está llena, <c>ProtocolError(SessionBusy)</c> y la conexión sigue.</item>
/// <item><b>Ping</b> responde <c>Pong</c> con el mismo <c>pingId</c>; un <c>Pong</c> se ignora.</item>
/// <item><b>Gameplay:</b> pasa por <see cref="CommandAdmitter"/>; si se rechaza, <c>CommandRejected</c> (D-075).</item>
/// <item><b>Fallos del parser:</b> <c>ProtocolError</c>, o <c>CommandRejected(InvalidCommand)</c> si el comando traía un <c>messageId</c> válido.
/// Un <c>Hello</c> con otra versión, además, cierra la conexión con <c>UnsupportedVersion</c>.</item>
/// <item><b>Faltas (D-074):</b> los fallos del parser, <c>HelloRequired</c>, <c>DuplicateHello</c> y <c>RateLimited</c> se registran
/// en el <see cref="IViolationTracker"/>. No son faltas <c>MatchNotRunning</c>, <c>NotRegistered</c>, <c>CommandQueueFull</c> ni
/// <c>SessionBusy</c>, que no son culpa del cliente. Al superar el límite, el cliente recibe la respuesta a su mensaje y después se
/// cierra con <c>AbuseDetected</c>.</item>
/// </list>
/// Todavía no mide el heartbeat.
/// </summary>
internal sealed class InboundRouter
{
  private readonly ConnectionId _connection;
  private readonly int _protocolVersion;
  private readonly ISessionEventSink _session;
  private readonly CommandAdmitter _admitter;
  private readonly IViolationTracker _violations;
  private readonly IConnectionOutput _output;

  public InboundRouter(
      ConnectionId connection,
      int protocolVersion,
      ISessionEventSink session,
      CommandAdmitter admitter,
      IViolationTracker violations,
      IConnectionOutput output)
  {
    _connection = connection;
    _protocolVersion = protocolVersion;
    _session = session;
    _admitter = admitter;
    _violations = violations;
    _output = output;
  }

  // Lo escribe el receptor y lo lee el bucle de aceptación para contar las conexiones pendientes (08 §3).
  private volatile bool _helloAccepted;

  /// <summary>Ya se informó el <c>Hello</c> a GameSession: la conexión pasó de <c>AwaitingHello</c> a <c>HelloAccepted</c>.</summary>
  public bool HelloAccepted
  {
    get => _helloAccepted;
    private set => _helloAccepted = value;
  }

  public void Handle(ParseResult result)
  {
    switch (result.Outcome)
    {
      case ParseOutcome.Parsed:
        Route(result.Message!);
        break;
      case ParseOutcome.ProtocolViolation:
        HandleViolation(result);
        break;
      case ParseOutcome.InvalidCommand:
        Send(ProtocolMessageTypes.CommandRejected, new CommandRejectedDto(result.ClientCommandId!.Value.Value, CommonErrors.InvalidCommand.Value));
        RegisterViolation(CommonErrors.InvalidCommand);
        break;
    }
  }

  private void Route(InboundMessage message)
  {
    switch (message)
    {
      case HelloMessage:
        OnHello();
        break;
      case PingMessage ping:
        Send(ProtocolMessageTypes.Pong, new PongDto(ping.PingId));
        break;
      case PongMessage:
        // Sin heartbeat todavía: un Pong se ignora, sin falta (08 §4).
        break;
      case ClientReadyMessage ready:
        OnClientReady(ready);
        break;
      case GameplayMessage gameplay:
        OnGameplay(gameplay);
        break;
    }
  }

  private void OnHello()
  {
    if (HelloAccepted)
    {
      SendProtocolError(NetworkErrors.DuplicateHello, null);
      RegisterViolation(NetworkErrors.DuplicateHello);
      return;
    }

    if (!_session.TryPostHello(_connection))
    {
      _output.Close(NetworkErrors.SessionBusy);
      return;
    }

    HelloAccepted = true;
  }

  private void OnClientReady(ClientReadyMessage ready)
  {
    if (!HelloAccepted)
    {
      SendProtocolError(NetworkErrors.HelloRequired, null);
      RegisterViolation(NetworkErrors.HelloRequired);
      return;
    }

    if (!_session.TryPostClientReady(_connection, ready.Initialization))
    {
      SendProtocolError(NetworkErrors.SessionBusy, null);
    }
  }

  private void OnGameplay(GameplayMessage gameplay)
  {
    if (!HelloAccepted)
    {
      SendProtocolError(NetworkErrors.HelloRequired, gameplay.ClientCommandId.Value);
      RegisterViolation(NetworkErrors.HelloRequired);
      return;
    }

    AdmissionDecision decision = _admitter.Admit(gameplay);
    if (!decision.IsAdmitted)
    {
      ErrorCode error = decision.Error!.Value;
      Send(ProtocolMessageTypes.CommandRejected, new CommandRejectedDto(gameplay.ClientCommandId.Value, error.Value));

      // De los rechazos de la admisión, solo el límite por segundo es culpa del cliente (08 §6).
      if (error == NetworkErrors.RateLimited)
      {
        RegisterViolation(error);
      }
    }
  }

  private void HandleViolation(ParseResult result)
  {
    ErrorCode error = result.Error!.Value;
    bool unsupportedVersion = error == NetworkErrors.UnsupportedVersion;

    // El cliente se entera de la versión del servidor para poder actualizarse (08 §2).
    SendProtocolError(error, result.MessageId, result.Detail, unsupportedVersion ? _protocolVersion : null);

    // Un Hello con otra versión cierra la conexión con ese motivo, sin contar la falta; cualquier otro mensaje con otra versión es
    // solo una falta (08 §3).
    if (unsupportedVersion && result.MessageType == ProtocolMessageTypes.Hello)
    {
      _output.Close(NetworkErrors.UnsupportedVersion);
      return;
    }

    RegisterViolation(error);
  }

  /// <summary>Registra una falta; si se superó el límite, cierra con <c>AbuseDetected</c> (D-074). Va después de responder.</summary>
  private void RegisterViolation(ErrorCode violation)
  {
    if (_violations.Register(violation))
    {
      _output.Close(NetworkErrors.AbuseDetected);
    }
  }

  private void SendProtocolError(ErrorCode error, long? messageId, string? detail = null, int? serverProtocolVersion = null) =>
      Send(ProtocolMessageTypes.ProtocolError, new ProtocolErrorDto(error.Value, messageId, detail, serverProtocolVersion));

  private void Send(string type, IOutboundPayload payload) =>
      _output.Send(new OutboundMessage(type, payload, DeliveryClass.Essential));
}
