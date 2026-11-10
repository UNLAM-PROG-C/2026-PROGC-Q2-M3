using Rts.Shared.Ids;

namespace Rts.Economy.Model;

/// <summary>
/// Trabajador habilitado para recolectar en el snapshot: tiene la orden <c>GatherWood</c> activa sobre <see cref="TreeId"/>
/// y está dentro del rango de interacción según Units / Movement. Simulation hace ese filtrado; los trabajadores
/// que todavía caminan hacia el árbol no se incluyen.
/// <para>
/// <see cref="TicksAccumulated"/>: ticks en rango acumulados del intervalo actual (D-013). Forma parte del estado
/// de la orden; Simulation lo reinicia a 0 cuando la orden cambia (01 §3.8).
/// </para>
/// </summary>
public sealed record GathererState
{
    public GathererState(EntityId workerId, PlayerId owner, EntityId treeId, int ticksAccumulated)
    {
        if (workerId.IsNone)
        {
            throw new ArgumentException("El trabajador debe tener un EntityId válido.", nameof(workerId));
        }

        if (!owner.IsValid)
        {
            throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
        }

        if (treeId.IsNone)
        {
            throw new ArgumentException("El árbol objetivo debe tener un EntityId válido.", nameof(treeId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ticksAccumulated);

        WorkerId = workerId;
        Owner = owner;
        TreeId = treeId;
        TicksAccumulated = ticksAccumulated;
    }

    public EntityId WorkerId { get; }

    public PlayerId Owner { get; }

    public EntityId TreeId { get; }

    public int TicksAccumulated { get; }
}
