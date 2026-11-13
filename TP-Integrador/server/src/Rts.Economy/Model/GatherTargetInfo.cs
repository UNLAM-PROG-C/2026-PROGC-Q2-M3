using Rts.Shared.Entities;
using Rts.Shared.Ids;

namespace Rts.Economy.Model;

/// <summary>
/// Datos mínimos del objetivo de una orden <c>GatherWood</c>, para validarla al procesar el comando (fase 1, RECOMENDADO D-092).
/// <see cref="TreeStatus"/> solo está presente si el objetivo es un árbol.
/// </summary>
public sealed record GatherTargetInfo
{
  public GatherTargetInfo(EntityId id, EntityKind kind, TreeStatus? treeStatus)
  {
    if (id.IsNone)
    {
      throw new ArgumentException("El objetivo debe tener un EntityId válido.", nameof(id));
    }

    if ((kind == EntityKind.Tree) != (treeStatus is not null))
    {
      throw new ArgumentException("El estado de árbol existe exactamente cuando el objetivo es un árbol.", nameof(treeStatus));
    }

    Id = id;
    Kind = kind;
    TreeStatus = treeStatus;
  }

  public EntityId Id { get; }

  public EntityKind Kind { get; }

  public TreeStatus? TreeStatus { get; }
}
