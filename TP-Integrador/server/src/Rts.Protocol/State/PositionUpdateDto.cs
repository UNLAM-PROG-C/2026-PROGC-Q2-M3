using Rts.Protocol.Outbound;

namespace Rts.Protocol.State;

/// <summary>
/// Posiciones oficiales de todas las unidades vivas al cierre de <see cref="Tick"/> (09 §5, RECOMENDADO D-070). Igual para ambos
/// jugadores. Se encola como <see cref="DeliveryClass.Supersedable"/>. Godot descarta las de un tick menor o igual al último
/// aplicado e ignora IDs que no conoce o que ya fueron destruidos.
/// </summary>
public sealed record PositionUpdateDto(long Tick, IReadOnlyList<UnitPositionDto> Units) : IOutboundPayload;

/// <summary>Posición de una unidad, redondeada a <c>StateSyncConfig.PositionDecimals</c>.</summary>
public sealed record UnitPositionDto(int Id, double X, double Z);
