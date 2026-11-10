using Rts.Shared.Ids;

namespace Rts.Protocol.Outbound;

/// <summary>Marca un DTO que puede viajar como payload de un mensaje de salida.</summary>
public interface IOutboundPayload
{
}

/// <summary>Cómo trata la cola de salida de una conexión a un mensaje (DECIDIDO D-064).</summary>
public enum DeliveryClass
{
    /// <summary>
    /// Nunca se descarta ni se reordena. Si la cola de un cliente no puede aceptar mensajes esenciales, Networking aplica su
    /// política de saturación y desconecta (08 §5).
    /// </summary>
    Essential = 1,

    /// <summary>
    /// Reemplazable: si en la cola hay un mensaje del mismo <see cref="OutboundMessage.Type"/> que todavía no empezó a enviarse,
    /// se quita y el nuevo se agrega <b>al final</b>. Como el nuevo nunca se adelanta a nada, la consolidación no atraviesa
    /// barreras estructurales (09 §7). Solo lo usa <c>PositionUpdate</c>.
    /// </summary>
    Supersedable = 2,
}

/// <summary>Mensaje listo para la cola de salida. Networking lo envuelve en su sobre y lo serializa en el escritor de la conexión.</summary>
public sealed record OutboundMessage
{
    public OutboundMessage(string type, IOutboundPayload payload, DeliveryClass delivery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);
        if (!Enum.IsDefined(delivery))
        {
            throw new ArgumentOutOfRangeException(nameof(delivery), delivery, "Clase de entrega desconocida.");
        }

        Type = type;
        Payload = payload;
        Delivery = delivery;
    }

    public string Type { get; }

    public IOutboundPayload Payload { get; }

    public DeliveryClass Delivery { get; }
}

/// <summary>
/// Colas de salida por cliente, implementadas por Networking (D-064, D-069). State Sync encola por <see cref="PlayerId"/>; Networking
/// resuelve la conexión con <c>ISessionGate.GetConnection</c> y descarta el mensaje si el jugador ya no está conectado.
/// <para>
/// <see cref="Enqueue"/> <b>no bloquea</b> y no hace I/O: se llama desde el hilo de Simulation en la fase 9. Para un mismo
/// destinatario, el orden de llamada es el orden de envío.
/// </para>
/// </summary>
public interface IClientOutbox
{
    void Enqueue(PlayerId recipient, OutboundMessage message);
}
