using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Networking.Admission;

/// <summary>
/// Resultado de <see cref="CommandAdmitter.Admit"/>: el comando entró a la cola global, o se rechazó con un código de error.
/// <see cref="IsViolation"/> dice si el rechazo cuenta como falta (D-074); solo lo es el límite por segundo.
/// Quien recibe la decisión (el receptor) envía el <c>CommandRejected</c> y cuenta la falta: la admisión no toca el socket.
/// </summary>
internal sealed record AdmissionDecision
{
  private AdmissionDecision(CommandSequence? sequence, ErrorCode? error, bool isViolation)
  {
    Sequence = sequence;
    Error = error;
    IsViolation = isViolation;
  }

  public bool IsAdmitted => Error is null;

  /// <summary>Sequence que asignó la cola global; solo si el comando fue admitido.</summary>
  public CommandSequence? Sequence { get; }

  /// <summary>Motivo del rechazo; <c>null</c> si fue admitido.</summary>
  public ErrorCode? Error { get; }

  /// <summary>El rechazo cuenta como falta del cliente. Siempre falso si fue admitido.</summary>
  public bool IsViolation { get; }

  public static AdmissionDecision Admitted(CommandSequence sequence) => new(sequence, null, false);

  public static AdmissionDecision Rejected(ErrorCode error, bool isViolation)
  {
    if (error.IsDefault)
    {
      throw new ArgumentException("Un rechazo requiere un código de error.", nameof(error));
    }

    return new AdmissionDecision(null, error, isViolation);
  }
}
