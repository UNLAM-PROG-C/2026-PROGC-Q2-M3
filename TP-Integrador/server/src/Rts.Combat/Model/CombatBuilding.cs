using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Combat.Model;

/// <summary>
/// Vista de combate inmutable de un edificio existente en el snapshot, completado o en obra.
/// Es objetivo válido de soldados enemigos (02 §2); las torres no atacan edificios.
/// <para>
/// <see cref="Hp"/> ya incluye el progreso de obra confirmado en la fase Construction de este tick (02 §4).
/// <see cref="OperationalFromTick"/> es nulo si el edificio está en obra; si se completó en el tick N vale N+1 (03 §3).
/// </para>
/// </summary>
public sealed record CombatBuilding
{
    public CombatBuilding(
        EntityId id,
        PlayerId owner,
        BuildingType type,
        Footprint footprint,
        int hp,
        TickId? operationalFromTick)
    {
        if (id.IsNone)
        {
            throw new ArgumentException("El edificio debe tener un EntityId válido.", nameof(id));
        }

        if (!owner.IsValid)
        {
            throw new ArgumentException($"Propietario inválido: {owner}.", nameof(owner));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentException($"Tipo de edificio inválido: {type}.", nameof(type));
        }

        if (footprint.Size.CellCount <= 0)
        {
            throw new ArgumentException("La huella no puede estar vacía.", nameof(footprint));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hp);

        Id = id;
        Owner = owner;
        Type = type;
        Footprint = footprint;
        Hp = hp;
        OperationalFromTick = operationalFromTick;
    }

    public EntityId Id { get; }

    public PlayerId Owner { get; }

    public BuildingType Type { get; }

    public Footprint Footprint { get; }

    /// <summary>HP actual (&gt; 0). En una obra también representa el progreso de construcción.</summary>
    public int Hp { get; }

    /// <summary>Primer tick operativo; nulo mientras está en obra.</summary>
    public TickId? OperationalFromTick { get; }

    public bool IsCompleted => OperationalFromTick is not null;

    /// <summary>Verdadero si el edificio está completado y ya es operativo en <paramref name="tick"/>.</summary>
    public bool IsOperationalAt(TickId tick) => OperationalFromTick is { } from && tick >= from;
}
