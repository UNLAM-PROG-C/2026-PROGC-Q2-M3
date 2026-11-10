using Rts.Shared.Entities;
using Rts.Shared.Ids;

namespace Rts.Production.Model;

/// <summary>
/// Solicitud de entrenamiento admitida (05 §7). <see cref="Sequence"/> es la Sequence global del comando TrainUnit:
/// identifica la solicitud y define su prioridad entre productores (05 §2-3, D-041).
/// </summary>
public sealed record ProductionRequest
{
  public ProductionRequest(CommandSequence sequence, EntityId producerId, PlayerId owner, UnitType unitType)
  {
    if (sequence.Value <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sequence), "La Sequence de admisión es positiva.");
    }

    if (producerId.IsNone)
    {
      throw new ArgumentException("El productor debe tener un EntityId válido.", nameof(producerId));
    }

    if (!owner.IsValid)
    {
      throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
    }

    if (!Enum.IsDefined(unitType))
    {
      throw new ArgumentException($"Tipo de unidad inválido: {unitType}.", nameof(unitType));
    }

    Sequence = sequence;
    ProducerId = producerId;
    Owner = owner;
    UnitType = unitType;
  }

  public CommandSequence Sequence { get; }

  public EntityId ProducerId { get; }

  public PlayerId Owner { get; }

  public UnitType UnitType { get; }
}

/// <summary>
/// Tarea activa de un productor: ya pagada (05 §3). Avanza un tick por fase Production desde el tick siguiente
/// al inicio (D-037). <see cref="RemainingTicks"/> = 0 significa <c>PendingSpawn</c>: terminó y espera celda,
/// sin volver a cobrar (05 §4).
/// </summary>
public sealed record ProductionJob
{
  public ProductionJob(ProductionRequest request, int paidFood, TickId startedAtTick, int remainingTicks)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentOutOfRangeException.ThrowIfNegative(paidFood);
    ArgumentOutOfRangeException.ThrowIfNegative(remainingTicks);

    Request = request;
    PaidFood = paidFood;
    StartedAtTick = startedAtTick;
    RemainingTicks = remainingTicks;
  }

  public ProductionRequest Request { get; }

  /// <summary>Comida cobrada al iniciar; no se reembolsa nunca (05 §6).</summary>
  public int PaidFood { get; }

  public TickId StartedAtTick { get; }

  public int RemainingTicks { get; }

  public bool IsPendingSpawn => RemainingTicks == 0;
}
