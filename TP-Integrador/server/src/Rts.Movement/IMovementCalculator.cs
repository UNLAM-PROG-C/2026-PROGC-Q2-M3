using Rts.Movement.Contracts;

namespace Rts.Movement;

/// <summary>
/// Avance oficial de un tick (04 §7, <c>CalculateTick</c>), en la fase 2. Contrato:
/// <list type="bullet">
/// <item>Función pura y determinista; no muta la entrada ni hace I/O. No crea hilos por unidad.</item>
/// <item>Nunca supera la distancia máxima por tick del tipo de unidad ni sale del trayecto validado (04 §8).</item>
/// <item>Aplica las reglas del contrato 04 §5 (revalidación por MapRevision, RepathPending y timeout, llegada,
/// mantener la tarea en rango, persecución directa y pedido de repath).</item>
/// </list>
/// </summary>
public interface IMovementCalculator
{
    MovementTickResult CalculateTick(MovementTickInput input);
}
