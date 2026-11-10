using Rts.Shared.Collections;
using Rts.Shared.Errors;
using Rts.Shared.Ids;

namespace Rts.Shared.Commands;

/// <summary>
/// Resultado oficial de un comando procesado por Simulation (06 §4, §8). Se conserva para responder
/// a duplicados con el mismo resultado (idempotencia). El constructor verifica la coherencia interna:
/// <list type="bullet">
/// <item><see cref="CommandOutcome.Accepted"/>: sin error global y sin entidades rechazadas.</item>
/// <item><see cref="CommandOutcome.PartiallyAccepted"/>: sin error global; al menos una entidad aceptada y una rechazada.</item>
/// <item><see cref="CommandOutcome.Rejected"/>: con error global y sin entidades aceptadas.</item>
/// </list>
/// </summary>
public sealed record CommandResult
{
  public CommandResult(
      IdempotencyKey key,
      CommandSequence sequence,
      TickId tick,
      CommandOutcome outcome,
      ErrorCode? error,
      EquatableArray<EntityCommandResult> entityResults)
  {
    Validate(outcome, error, entityResults);
    Key = key;
    Sequence = sequence;
    Tick = tick;
    Outcome = outcome;
    Error = error;
    EntityResults = entityResults;
  }

  public IdempotencyKey Key { get; }

  public CommandSequence Sequence { get; }

  /// <summary>Tick en que Simulation procesó el comando.</summary>
  public TickId Tick { get; }

  public CommandOutcome Outcome { get; }

  /// <summary>Motivo global; solo presente si <see cref="Outcome"/> es <see cref="CommandOutcome.Rejected"/>.</summary>
  public ErrorCode? Error { get; }

  /// <summary>Resultados por entidad para comandos grupales; vacío si el comando no es grupal.</summary>
  public EquatableArray<EntityCommandResult> EntityResults { get; }

  public static CommandResult Accepted(
      CommandEnvelope command,
      TickId tick,
      EquatableArray<EntityCommandResult> entityResults = default) =>
      new(command.Key, command.Sequence, tick, CommandOutcome.Accepted, null, entityResults);

  public static CommandResult PartiallyAccepted(
      CommandEnvelope command,
      TickId tick,
      EquatableArray<EntityCommandResult> entityResults) =>
      new(command.Key, command.Sequence, tick, CommandOutcome.PartiallyAccepted, null, entityResults);

  public static CommandResult Rejected(
      CommandEnvelope command,
      TickId tick,
      ErrorCode error,
      EquatableArray<EntityCommandResult> entityResults = default) =>
      new(command.Key, command.Sequence, tick, CommandOutcome.Rejected, error, entityResults);

  private static void Validate(CommandOutcome outcome, ErrorCode? error, EquatableArray<EntityCommandResult> entityResults)
  {
    if (error is { IsDefault: true })
    {
      throw new ArgumentException("El código de error no puede ser default(ErrorCode).", nameof(error));
    }

    bool anyAccepted = false;
    bool anyRejected = false;
    foreach (EntityCommandResult result in entityResults)
    {
      ArgumentNullException.ThrowIfNull(result, nameof(entityResults));
      anyAccepted |= result.IsAccepted;
      anyRejected |= !result.IsAccepted;
    }

    bool valid = outcome switch
    {
      CommandOutcome.Accepted => error is null && !anyRejected,
      CommandOutcome.PartiallyAccepted => error is null && anyAccepted && anyRejected,
      CommandOutcome.Rejected => error is not null && !anyAccepted,
      _ => false,
    };

    if (!valid)
    {
      throw new ArgumentException(
          $"CommandResult incoherente: Outcome={outcome}, Error={error?.ToString() ?? "null"}, " +
          $"aceptadas={anyAccepted}, rechazadas={anyRejected}.");
    }
  }
}
