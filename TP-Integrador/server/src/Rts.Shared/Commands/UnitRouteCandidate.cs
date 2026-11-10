using Rts.Shared.Collections;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Shared.Commands;

/// <summary>
/// Ruta candidata que Godot propone para una unidad (04 §4, D-026). <see cref="Waypoints"/> no incluye la posición actual:
/// el servidor valida desde la posición oficial de la unidad. Puede estar vacía si la unidad ya está en destino o en rango.
/// El cliente nunca informa posiciones oficiales; solo propone el trayecto.
/// </summary>
public sealed record UnitRouteCandidate
{
    public UnitRouteCandidate(EntityId unitId, EquatableArray<PositionXZ> waypoints)
    {
        if (unitId.IsNone)
        {
            throw new ArgumentException("La ruta debe indicar una unidad válida.", nameof(unitId));
        }

        CommandGuards.EnsureFinite(waypoints, nameof(waypoints));
        UnitId = unitId;
        Waypoints = waypoints;
    }

    public EntityId UnitId { get; }

    public EquatableArray<PositionXZ> Waypoints { get; }
}

/// <summary>Validaciones estructurales comunes de los payloads de comando (no son reglas de gameplay).</summary>
internal static class CommandGuards
{
    public static void EnsureFinite(EquatableArray<PositionXZ> positions, string paramName)
    {
        foreach (PositionXZ position in positions)
        {
            if (!position.IsFinite)
            {
                throw new ArgumentException("Las coordenadas deben ser finitas.", paramName);
            }
        }
    }

    public static void EnsureFinite(PositionXZ position, string paramName)
    {
        if (!position.IsFinite)
        {
            throw new ArgumentException("Las coordenadas deben ser finitas.", paramName);
        }
    }

    public static void EnsureEntity(EntityId id, string paramName)
    {
        if (id.IsNone)
        {
            throw new ArgumentException("Se requiere un EntityId válido.", paramName);
        }
    }

    /// <summary>Al menos una ruta y una sola ruta por unidad.</summary>
    public static void EnsureRoutes(EquatableArray<UnitRouteCandidate> routes, string paramName)
    {
        if (routes.IsEmpty)
        {
            throw new ArgumentException("El comando debe incluir al menos una unidad.", paramName);
        }

        var seen = new HashSet<EntityId>();
        foreach (UnitRouteCandidate route in routes)
        {
            ArgumentNullException.ThrowIfNull(route, paramName);
            if (!seen.Add(route.UnitId))
            {
                throw new ArgumentException($"Unidad repetida en el comando: {route.UnitId}.", paramName);
            }
        }
    }
}
