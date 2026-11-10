using Rts.Shared.Errors;

namespace Rts.Movement.Contracts;

/// <summary>
/// Resultado de validar una ruta candidata (04 §4, <c>ValidateRoute</c>). Si es inválida, <see cref="FailedSegmentIndex"/>
/// indica el primer tramo que falla (0 = de la posición actual al primer waypoint), para diagnóstico.
/// </summary>
public sealed record RouteValidation
{
  public static readonly RouteValidation Valid = new(null, null);

  private RouteValidation(ErrorCode? error, int? failedSegmentIndex)
  {
    Error = error;
    FailedSegmentIndex = failedSegmentIndex;
  }

  public bool IsValid => Error is null;

  public ErrorCode? Error { get; }

  public int? FailedSegmentIndex { get; }

  public static RouteValidation Invalid(ErrorCode error, int? failedSegmentIndex = null)
  {
    if (error.IsDefault)
    {
      throw new ArgumentException("Una ruta inválida requiere código de error.", nameof(error));
    }

    if (failedSegmentIndex is < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(failedSegmentIndex));
    }

    return new RouteValidation(error, failedSegmentIndex);
  }
}
