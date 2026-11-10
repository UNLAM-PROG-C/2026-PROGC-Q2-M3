namespace Rts.Shared.Commands;

/// <summary>Resultado global de un comando (06 §4).</summary>
public enum CommandOutcome
{
  Accepted = 1,
  PartiallyAccepted = 2,
  Rejected = 3,
}
