using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Session;
using Rts.Shared.Session;

namespace Rts.Networking.Outbound;

/// <summary>
/// Convierte los mensajes de sesión de GameSession (contrato 07 §9) en su <see cref="OutboundMessage"/>. Los cinco son
/// <see cref="DeliveryClass.Essential"/>: nunca se descartan ni se reordenan (D-064).
/// <para>
/// Un mensaje que no sea uno de los cinco lanza <see cref="ArgumentException"/>: así un tipo nuevo no se pierde en silencio.
/// Es una elección de implementación, no una decisión del contrato.
/// </para>
/// </summary>
public sealed class SessionMessageEncoder : ISessionMessageEncoder
{
  public OutboundMessage Encode(ISessionMessage message)
  {
    ArgumentNullException.ThrowIfNull(message);

    return message switch
    {
      PlayerAssigned m => Essential(
          ProtocolMessageTypes.PlayerAssigned,
          new PlayerAssignedDto(m.Session.ToString(), m.Player.Value)),
      StartAborted m => Essential(
          ProtocolMessageTypes.StartAborted,
          new StartAbortedDto(m.Initialization.Value, m.Reason)),
      MatchStarted m => Essential(
          ProtocolMessageTypes.MatchStarted,
          new MatchStartedDto(m.Session.ToString(), m.Initialization.Value)),
      MatchEnded m => Essential(
          ProtocolMessageTypes.MatchEnded,
          new MatchEndedDto(
              m.Session.ToString(),
              m.Result.ResultType,
              m.Result.Winner?.Value,
              m.Result.Reason,
              m.Result.FinalTick?.Value)),
      SessionRequestRejected m => Essential(
          ProtocolMessageTypes.SessionRequestRejected,
          new SessionRequestRejectedDto(m.Reason.Value, m.Initialization?.Value)),
      _ => throw new ArgumentException(
          $"El mensaje de sesión '{message.GetType().Name}' no tiene codificación.",
          nameof(message)),
    };
  }

  private static OutboundMessage Essential(string type, IOutboundPayload payload) =>
      new(type, payload, DeliveryClass.Essential);
}
