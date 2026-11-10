using Rts.Protocol.Outbound;
using Rts.Shared.Session;

namespace Rts.Networking.Outbound;

/// <summary>Resultado de encolar en la cola de salida de una conexión.</summary>
public enum EnqueueOutcome
{
    /// <summary>Encolado al final.</summary>
    Enqueued = 1,

    /// <summary>Encolado al final, y se quitó un reemplazable del mismo tipo que todavía no empezó a enviarse (D-064).</summary>
    Superseded = 2,

    /// <summary>Un esencial no entró: quien encola debe cerrar la conexión con <c>Network.OutboundSaturated</c> (D-073).</summary>
    Saturated = 3,

    /// <summary>La conexión está cerrándose y ya no acepta mensajes; se descarta (no hay entrega a sockets muertos).</summary>
    Closed = 4,
}

/// <summary>
/// Cola de salida acotada de una conexión (08 §5). <b>Varios productores</b> (hilo de Simulation, receptor, host de GameSession,
/// monitor de heartbeat) y <b>un solo consumidor</b>, el <c>SendLoop</c>, que es el único que escribe en el socket.
/// <list type="bullet">
/// <item><see cref="TryEnqueue"/> no bloquea nunca. El orden de encolado es el orden de envío.</item>
/// <item>Capacidad en cantidad de mensajes (<c>OutboundQueueCapacity</c>). Un esencial que no entra devuelve <c>Saturated</c>: nunca se
/// descarta un esencial para hacerle lugar (D-073).</item>
/// <item>Reemplazable: si hay uno del mismo tipo pendiente que el <c>SendLoop</c> todavía no tomó, se quita y el nuevo va al final
/// (D-064). Un mensaje que el <c>SendLoop</c> ya tomó no se toca (09 §7).</item>
/// <item>RECOMENDADO (D-078): lista enlazada protegida por un lock más una señal para el consumidor, porque <c>Channel</c> no permite
/// quitar un elemento intermedio.</item>
/// </list>
/// </summary>
public interface IOutboundQueue
{
    EnqueueOutcome TryEnqueue(OutboundMessage message);

    /// <summary>Espera el próximo mensaje. Devuelve <c>null</c> cuando la cola está completada y vacía.</summary>
    ValueTask<OutboundMessage?> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>Cantidad de mensajes pendientes (métricas).</summary>
    int Count { get; }

    /// <summary>Deja de aceptar mensajes; los ya encolados se siguen entregando hasta vaciar la cola.</summary>
    void Complete();
}

/// <summary>Convierte un mensaje de sesión (contrato 07) en su DTO. Todos son <see cref="DeliveryClass.Essential"/>.</summary>
public interface ISessionMessageEncoder
{
    OutboundMessage Encode(ISessionMessage message);
}
