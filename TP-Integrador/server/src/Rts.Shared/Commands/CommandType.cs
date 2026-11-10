namespace Rts.Shared.Commands;

/// <summary>
/// Tipos de comando de gameplay (08 §3). El valor 0 no está definido a propósito.
/// <see cref="RepathResponse"/> entra por la misma cola global con Sequence (D-028).
/// </summary>
public enum CommandType
{
    Move = 1,
    Stop = 2,
    Attack = 3,
    GatherWood = 4,
    Build = 5,
    RepairFarm = 6,
    TrainUnit = 7,
    RepathResponse = 8,
}
