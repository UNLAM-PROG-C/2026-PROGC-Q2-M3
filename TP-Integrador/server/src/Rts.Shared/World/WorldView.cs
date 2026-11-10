using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;

namespace Rts.Shared.World;

// Modelo de lectura inmutable del mundo confirmado (RECOMENDADO D-049). Simulation lo produce; State Sync lo usa para el
// InitialState (09 §3). Contiene datos PRIVADOS (recursos y producción de cada jugador): filtrar por destinatario es
// responsabilidad de State Sync. No es un DTO JSON.

/// <summary>Vista completa del mundo en un tick confirmado.</summary>
public sealed record WorldView(
    TickId Tick,
    MapView Map,
    EquatableArray<PlayerView> Players,
    EquatableArray<UnitView> Units,
    EquatableArray<BuildingView> Buildings,
    EquatableArray<TreeView> Trees,
    EquatableArray<ProducerView> Producers);

/// <summary>Grilla y revisión del mapa. Los obstáculos son las huellas de <see cref="WorldView.Buildings"/>; las reservas, las celdas de <see cref="WorldView.Trees"/>.</summary>
public sealed record MapView(GridSpec Grid, MapRevision Revision);

/// <summary>Saldo de un jugador. **Privado:** solo para su dueño.</summary>
public sealed record PlayerView(PlayerId Player, ResourceAmount Resources);

/// <summary>Unidad visible para ambos jugadores.</summary>
public sealed record UnitView(EntityId UnitId, PlayerId Owner, UnitType UnitType, PositionXZ Position, int Hp, UnitOrderKind Order);

/// <summary>Edificio visible para ambos jugadores. <see cref="Farm"/> solo para granjas completadas.</summary>
public sealed record BuildingView(
    EntityId BuildingId,
    PlayerId Owner,
    BuildingType BuildingType,
    Footprint Footprint,
    int Hp,
    bool IsCompleted,
    FarmPublicState? Farm);

/// <summary>Estado público de una granja: agotamiento y reparación (09 §4, <c>FarmStateChanged</c>).</summary>
public sealed record FarmPublicState(bool IsDepleted, bool IsRepairing);

/// <summary>Árbol con su celda reservada y su posición (centro de la celda, D-050). Visible para ambos.</summary>
public sealed record TreeView(EntityId TreeId, GridCell Cell, PositionXZ Position, bool IsAvailable);

/// <summary>Estado de un productor. **Privado:** solo para su dueño (nunca se exponen colas rivales).</summary>
public sealed record ProducerView(
    EntityId ProducerId,
    PlayerId Owner,
    ProducerStatus Status,
    UnitType? ActiveUnitType,
    int? RemainingTicks,
    EquatableArray<UnitType> PendingUnitTypes);

/// <summary>Posición oficial de una unidad en un tick (base de las actualizaciones a 10 Hz, 09 §5).</summary>
public sealed record UnitPosition(EntityId UnitId, PositionXZ Position);
