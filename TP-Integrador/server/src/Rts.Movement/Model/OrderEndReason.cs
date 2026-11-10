namespace Rts.Movement.Model;

/// <summary>Motivo por el que Movement termina una orden y deja la unidad en Idle.</summary>
public enum OrderEndReason
{
    /// <summary>Una orden Move llegó a su destino (04 §2).</summary>
    Completed = 1,

    /// <summary>El objetivo de una orden de tarea ya no existe en el snapshot (caso defensivo, D-025).</summary>
    TargetLost = 2,

    /// <summary>No hay forma de llegar al objetivo según <c>CheckReachability</c> (04 §5).</summary>
    Unreachable = 3,

    /// <summary>Godot no entregó una ruta válida antes del timeout de repath (04 §5).</summary>
    RepathTimeout = 4,
}
