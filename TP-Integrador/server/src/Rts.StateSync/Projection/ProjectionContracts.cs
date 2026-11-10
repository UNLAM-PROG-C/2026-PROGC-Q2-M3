using Rts.Protocol.Outbound;
using Rts.Shared.Collections;
using Rts.Shared.Ids;
using Rts.Shared.World;

namespace Rts.StateSync.Projection;

/// <summary>Datos para armar el InitialState de un destinatario (09 §3).</summary>
public sealed record InitialStateRequest
{
    public InitialStateRequest(SessionId session, InitializationId initialization, PlayerId recipient, WorldView world, GameCatalog catalog)
    {
        if (!recipient.IsValid)
        {
            throw new ArgumentException($"Destinatario inválido: {recipient}.", nameof(recipient));
        }

        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);

        Session = session;
        Initialization = initialization;
        Recipient = recipient;
        World = world;
        Catalog = catalog;
    }

    public SessionId Session { get; }

    public InitializationId Initialization { get; }

    public PlayerId Recipient { get; }

    public WorldView World { get; }

    public GameCatalog Catalog { get; }
}

/// <summary>Mensajes de un tick para un destinatario, en orden de encolado: primero <c>StateEvents</c> y después <c>PositionUpdate</c>.</summary>
public sealed record RecipientMessages(PlayerId Recipient, EquatableArray<OutboundMessage> Messages);

/// <summary>Proyección completa de un tick: siempre P1 y después P2, aunque alguno no tenga mensajes.</summary>
public sealed record TickProjection(TickId Tick, RecipientMessages Player1, RecipientMessages Player2);
