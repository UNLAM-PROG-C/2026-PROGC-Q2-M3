namespace Rts.Shared.Entities;

/// <summary>Categoría de una entidad del mundo. Todas comparten el espacio global de <c>EntityId</c> (D-003).</summary>
public enum EntityKind
{
    Unit = 1,
    Building = 2,
    Tree = 3,
}
