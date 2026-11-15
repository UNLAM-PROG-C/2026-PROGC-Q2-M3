using Rts.Networking.Outbound;
using Rts.Protocol;
using Rts.Protocol.Outbound;
using Rts.Protocol.Session;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.Networking.Tests.Outbound;

public class SessionMessageEncoderTests
{
  private static readonly SessionId Session = new(Guid.Parse("5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41"));
  private static readonly InitializationId Initialization = new(3);

  private readonly SessionMessageEncoder _encoder = new();

  [Fact]
  public void Encode_PlayerAssigned_UsesItsTypeAndDto()
  {
    var encoded = _encoder.Encode(new PlayerAssigned(Session, PlayerId.Two));

    Assert.Equal(ProtocolMessageTypes.PlayerAssigned, encoded.Type);
    var dto = Assert.IsType<PlayerAssignedDto>(encoded.Payload);
    Assert.Equal("5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41", dto.SessionId);
    Assert.Equal(2, dto.PlayerId);
  }

  [Theory]
  [InlineData(StartAbortReason.OpponentLeft)]
  [InlineData(StartAbortReason.ReadyTimeout)]
  public void Encode_StartAborted_UsesItsTypeAndDto(StartAbortReason reason)
  {
    var encoded = _encoder.Encode(new StartAborted(Initialization, reason));

    Assert.Equal(ProtocolMessageTypes.StartAborted, encoded.Type);
    var dto = Assert.IsType<StartAbortedDto>(encoded.Payload);
    Assert.Equal(3, dto.InitializationId);
    Assert.Equal(reason, dto.Reason);
  }

  [Fact]
  public void Encode_MatchStarted_UsesItsTypeAndDto()
  {
    var encoded = _encoder.Encode(new MatchStarted(Session, Initialization));

    Assert.Equal(ProtocolMessageTypes.MatchStarted, encoded.Type);
    var dto = Assert.IsType<MatchStartedDto>(encoded.Payload);
    Assert.Equal("5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41", dto.SessionId);
    Assert.Equal(3, dto.InitializationId);
  }

  [Fact]
  public void Encode_MatchEndedVictory_FlattensWinnerAndFinalTick()
  {
    var result = MatchResult.BaseDestroyed(PlayerId.One, new TickId(5400));

    var encoded = _encoder.Encode(new MatchEnded(Session, result));

    Assert.Equal(ProtocolMessageTypes.MatchEnded, encoded.Type);
    var dto = Assert.IsType<MatchEndedDto>(encoded.Payload);
    Assert.Equal("5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41", dto.SessionId);
    Assert.Equal(MatchResultType.Victory, dto.ResultType);
    Assert.Equal(1, dto.Winner);
    Assert.Equal(MatchEndReason.BaseDestroyed, dto.Reason);
    Assert.Equal(5400, dto.FinalTick);
  }

  [Fact]
  public void Encode_MatchEndedDraw_HasNoWinner()
  {
    var result = MatchResult.Draw(new TickId(5400));

    var dto = Assert.IsType<MatchEndedDto>(_encoder.Encode(new MatchEnded(Session, result)).Payload);

    Assert.Equal(MatchResultType.Draw, dto.ResultType);
    Assert.Null(dto.Winner);
    Assert.Equal(MatchEndReason.BothBasesDestroyed, dto.Reason);
    Assert.Equal(5400, dto.FinalTick);
  }

  [Fact]
  public void Encode_MatchEndedWithoutConfirmedTicks_HasNoFinalTick()
  {
    var result = MatchResult.BothPlayersDisconnected(null);

    var dto = Assert.IsType<MatchEndedDto>(_encoder.Encode(new MatchEnded(Session, result)).Payload);

    Assert.Equal(MatchResultType.TechnicalEnd, dto.ResultType);
    Assert.Null(dto.Winner);
    Assert.Null(dto.FinalTick);
  }

  [Fact]
  public void Encode_SessionRequestRejected_UsesTheErrorCodeAsString()
  {
    var message = new SessionRequestRejected(new ErrorCode("Session.StaleInitialization"), Initialization);

    var encoded = _encoder.Encode(message);

    Assert.Equal(ProtocolMessageTypes.SessionRequestRejected, encoded.Type);
    var dto = Assert.IsType<SessionRequestRejectedDto>(encoded.Payload);
    Assert.Equal("Session.StaleInitialization", dto.Error);
    Assert.Equal(3, dto.InitializationId);
  }

  [Fact]
  public void Encode_SessionRequestRejectedWithoutInitialization_LeavesItNull()
  {
    var message = new SessionRequestRejected(new ErrorCode("Session.StaleInitialization"), null);

    var dto = Assert.IsType<SessionRequestRejectedDto>(_encoder.Encode(message).Payload);

    Assert.Null(dto.InitializationId);
  }

  [Fact]
  public void Encode_EverySessionMessage_IsEssential()
  {
    ISessionMessage[] messages =
    [
      new PlayerAssigned(Session, PlayerId.One),
      new StartAborted(Initialization, StartAbortReason.ReadyTimeout),
      new MatchStarted(Session, Initialization),
      new MatchEnded(Session, MatchResult.Draw(new TickId(1))),
      new SessionRequestRejected(new ErrorCode("Session.StaleInitialization"), null),
    ];

    foreach (var message in messages)
    {
      Assert.Equal(DeliveryClass.Essential, _encoder.Encode(message).Delivery);
    }
  }

  [Fact]
  public void Encode_Null_Throws()
  {
    Assert.Throws<ArgumentNullException>(() => _encoder.Encode(null!));
  }

  [Fact]
  public void Encode_UnknownMessage_ThrowsInsteadOfDroppingItSilently()
  {
    Assert.Throws<ArgumentException>(() => _encoder.Encode(new UnknownSessionMessage()));
  }

  private sealed record UnknownSessionMessage : ISessionMessage;
}
