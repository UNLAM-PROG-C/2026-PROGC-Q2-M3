using Rts.Protocol.State;
using Rts.Shared.World;

namespace Rts.StateSync.Projection;

/// <summary>
/// Proyección pura de lo confirmado a DTOs por destinatario (09 §9). <b>Sin estado y sin I/O</b>: el resultado depende solo de la
/// entrada y de la configuración, así que dos llamadas con la misma publicación dan los mismos mensajes. No mantiene un
/// segundo mundo (09 §1). Reglas en el contrato 09 §3-5.
/// </summary>
public interface IStateProjector
{
    /// <summary>
    /// InitialState de un destinatario: todo lo público del mundo, más los recursos y productores <b>solo</b> del destinatario, y el
    /// catálogo.
    /// </summary>
    InitialStateDto BuildInitialState(InitialStateRequest request);

    /// <summary>
    /// Mensajes de un tick por destinatario:
    /// <list type="number">
    /// <item><c>StateEvents</c> con los eventos que el destinatario puede ver, en orden oficial, y los resultados de sus comandos.
    /// Se omite si ambas listas quedan vacías.</item>
    /// <item><c>PositionUpdate</c> con todas las unidades vivas, solo si el tick es múltiplo del intervalo (D-062), como
    /// <c>Supersedable</c>.</item>
    /// </list>
    /// </summary>
    TickProjection ProjectTick(TickPublication publication);
}
