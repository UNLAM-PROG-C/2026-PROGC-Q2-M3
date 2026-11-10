using Rts.Shared.Ids;

namespace Rts.Economy.Model;

/// <summary>
/// Vista económica inmutable de un árbol. Se usa como entrada (snapshot) y como estado propuesto en el resultado.
/// Posición y celdas reservadas no forman parte de esta vista: pertenecen al mapa.
/// Invariantes: <see cref="TreeStatus.Available"/> ⇒ madera &gt; 0 y sin tick de agotamiento;
/// <see cref="TreeStatus.Depleted"/> ⇒ madera = 0 y con tick de agotamiento.
/// </summary>
public sealed record TreeEconomyState
{
  public TreeEconomyState(EntityId treeId, int woodRemaining, TreeStatus status, TickId? depletedAtTick)
  {
    if (treeId.IsNone)
    {
      throw new ArgumentException("El árbol debe tener un EntityId válido.", nameof(treeId));
    }

    ArgumentOutOfRangeException.ThrowIfNegative(woodRemaining);

    bool valid = status switch
    {
      TreeStatus.Available => woodRemaining > 0 && depletedAtTick is null,
      TreeStatus.Depleted => woodRemaining == 0 && depletedAtTick is not null,
      _ => false,
    };

    if (!valid)
    {
      throw new ArgumentException(
          $"Estado de árbol incoherente: Status={status}, WoodRemaining={woodRemaining}, DepletedAtTick={depletedAtTick?.ToString() ?? "null"}.");
    }

    TreeId = treeId;
    WoodRemaining = woodRemaining;
    Status = status;
    DepletedAtTick = depletedAtTick;
  }

  public EntityId TreeId { get; }

  public int WoodRemaining { get; }

  public TreeStatus Status { get; }

  /// <summary>Tick en que se agotó; base para calcular la regeneración (contrato 01 §4).</summary>
  public TickId? DepletedAtTick { get; }

  public static TreeEconomyState Available(EntityId treeId, int woodRemaining) =>
      new(treeId, woodRemaining, TreeStatus.Available, null);

  public static TreeEconomyState Depleted(EntityId treeId, TickId depletedAtTick) =>
      new(treeId, 0, TreeStatus.Depleted, depletedAtTick);
}
