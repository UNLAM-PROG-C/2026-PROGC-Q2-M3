using Rts.Economy.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Shared.Resources;

namespace Rts.Economy.Contracts;

/// <summary>
/// Propuesta inmutable de Economy para un tick (01 §6). Simulation la valida y la confirma después de la barrera
/// Economy ∥ Combat; Economy no escribe <c>WorldState</c>. Las colecciones vienen en orden determinista
/// (ver contrato 01 §5), así que dos cálculos sobre la misma entrada producen resultados iguales.
/// </summary>
/// <param name="Tick">Tick de la entrada que originó la propuesta.</param>
/// <param name="PlayerDeltas">Recursos a acreditar, consolidados por jugador; solo deltas no nulos, por PlayerId ascendente.</param>
/// <param name="WoodGathered">Detalle de la madera obtenida por cada trabajador (por WorkerId ascendente).</param>
/// <param name="FoodProduced">Detalle de las tandas producidas por cada granja (por FarmId ascendente).</param>
/// <param name="GathererUpdates">Nuevo contador de los recolectores que siguen con su orden (por WorkerId ascendente).</param>
/// <param name="WorkersToIdle">Recolectores cuyo árbol se agotó en este tick: pasan a Idle y no reanudan solos (por ID ascendente).</param>
/// <param name="TreeUpdates">Nuevo estado de los árboles que cambiaron (por TreeId ascendente).</param>
/// <param name="FarmUpdates">Nuevo estado de las granjas que cambiaron (por FarmId ascendente).</param>
/// <param name="ProposedEvents">Eventos propuestos (<c>TreeDepleted</c>, <c>TreeRespawned</c>, <c>FarmDepleted</c>); Simulation los sella al confirmar.</param>
public sealed record EconomyTickResult(
    TickId Tick,
    EquatableArray<PlayerResourceDelta> PlayerDeltas,
    EquatableArray<WoodGathered> WoodGathered,
    EquatableArray<FoodProduced> FoodProduced,
    EquatableArray<GathererState> GathererUpdates,
    EquatableArray<EntityId> WorkersToIdle,
    EquatableArray<TreeEconomyState> TreeUpdates,
    EquatableArray<FarmEconomyState> FarmUpdates,
    EquatableArray<IDomainEventPayload> ProposedEvents)
{
    /// <summary>Resultado sin cambios para el tick indicado.</summary>
    public static EconomyTickResult Empty(TickId tick) =>
        new(tick, default, default, default, default, default, default, default, default);
}

/// <summary>Recursos a acreditar a un jugador en un tick.</summary>
public sealed record PlayerResourceDelta(PlayerId Player, ResourceAmount Delta);

/// <summary>Madera obtenida por un trabajador en un tick (puede ser parcial si el árbol no alcanzaba).</summary>
public sealed record WoodGathered(EntityId WorkerId, PlayerId Owner, EntityId TreeId, int Amount);

/// <summary>Comida producida por una granja en un tick (una tanda).</summary>
public sealed record FoodProduced(EntityId FarmId, PlayerId Owner, int Amount);
