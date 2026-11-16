using Rts.Networking.Admission;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Networking.Tests.Admission;

public class AdmissionDecisionTests
{
  [Fact]
  public void Admitted_HasTheSequenceAndNoError()
  {
    var decision = AdmissionDecision.Admitted(new CommandSequence(3));

    Assert.True(decision.IsAdmitted);
    Assert.Equal(new CommandSequence(3), decision.Sequence);
    Assert.Null(decision.Error);
    Assert.False(decision.IsViolation);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Rejected_HasTheErrorAndNoSequence(bool isViolation)
  {
    var decision = AdmissionDecision.Rejected(NetworkErrors.RateLimited, isViolation);

    Assert.False(decision.IsAdmitted);
    Assert.Equal(NetworkErrors.RateLimited, decision.Error);
    Assert.Null(decision.Sequence);
    Assert.Equal(isViolation, decision.IsViolation);
  }

  [Fact]
  public void Rejected_WithoutAnErrorCode_Throws()
  {
    Assert.Throws<ArgumentException>(() => AdmissionDecision.Rejected(default(ErrorCode), isViolation: false));
  }
}
