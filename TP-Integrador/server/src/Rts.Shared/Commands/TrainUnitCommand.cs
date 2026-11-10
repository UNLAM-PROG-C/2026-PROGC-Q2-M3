using Rts.Shared.Entities;
using Rts.Shared.Ids;

namespace Rts.Shared.Commands;

/// <summary>
/// Solicitar el entrenamiento de una unidad en un productor propio (05 §1-2). Se encola aunque no haya comida;
/// la comida se cobra al iniciar el entrenamiento, no al solicitar. No hay cancelación.
/// La solicitud queda identificada por la <c>Sequence</c> global de su <see cref="CommandEnvelope"/> (D-041).
/// </summary>
public sealed record TrainUnitCommand : IGameCommand
{
    public TrainUnitCommand(EntityId producerId, UnitType unitType)
    {
        CommandGuards.EnsureEntity(producerId, nameof(producerId));
        if (!Enum.IsDefined(unitType))
        {
            throw new ArgumentException($"Tipo de unidad inválido: {unitType}.", nameof(unitType));
        }

        ProducerId = producerId;
        UnitType = unitType;
    }

    public CommandType Type => CommandType.TrainUnit;

    public EntityId ProducerId { get; }

    public UnitType UnitType { get; }
}
