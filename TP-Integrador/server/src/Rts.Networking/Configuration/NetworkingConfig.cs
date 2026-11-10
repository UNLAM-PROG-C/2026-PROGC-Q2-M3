using System.Net;
using Rts.Protocol;

namespace Rts.Networking.Configuration;

/// <summary>
/// Parámetros de Networking (08 §10). Todos los configurables están aprobados; sus valores son PENDIENTES. Se valida una vez al
/// iniciar el servidor.
/// </summary>
public sealed record NetworkingConfig
{
    /// <summary>Dirección de escucha (por ejemplo <c>0.0.0.0</c> o <c>127.0.0.1</c>).</summary>
    public required string ListenAddress { get; init; }

    public required int Port { get; init; }

    /// <summary>Única versión aceptada; no hay negociación (08 §2).</summary>
    public int ProtocolVersion { get; init; } = NetworkEnvelope.InitialVersion;

    /// <summary>Máximo de bytes de una línea entrante, sin el delimitador. Superarlo cierra la conexión (D-074).</summary>
    public required int MaxInboundMessageBytes { get; init; }

    /// <summary>Conexiones aceptadas que todavía no enviaron <c>Hello</c>, como máximo al mismo tiempo.</summary>
    public required int MaxPendingConnections { get; init; }

    /// <summary>Plazo para recibir el <c>Hello</c> desde que se acepta el socket.</summary>
    public required TimeSpan HandshakeTimeout { get; init; }

    /// <summary>Comandos de gameplay admitidos por segundo y por cliente, en régimen sostenido (08 §4).</summary>
    public required int CommandsPerSecond { get; init; }

    /// <summary>Ráfaga máxima de comandos (capacidad del balde de fichas, D-077). Mayor o igual que <see cref="CommandsPerSecond"/>.</summary>
    public required int CommandBurst { get; init; }

    /// <summary>Mensajes en la cola de salida de cada cliente. Si un esencial no entra, se desconecta (D-073).</summary>
    public required int OutboundQueueCapacity { get; init; }

    /// <summary>Faltas toleradas dentro de <see cref="ViolationWindow"/>; una más desconecta (D-074).</summary>
    public required int MaxViolations { get; init; }

    public required TimeSpan ViolationWindow { get; init; }

    /// <summary>Tiempo sin entrada válida del cliente tras el cual el servidor envía <c>Ping</c> (08 §6).</summary>
    public required TimeSpan PingIdleInterval { get; init; }

    /// <summary>Plazo para recibir el <c>Pong</c> correspondiente; vencido, la conexión se considera perdida.</summary>
    public required TimeSpan PongTimeout { get; init; }

    /// <summary>Plazo para entregar el <c>Disconnecting</c> de un cierre individual antes de cortar el socket.</summary>
    public required TimeSpan CloseLinger { get; init; }

    /// <summary>Plazo para vaciar las colas al terminar la partida (07 §9).</summary>
    public required TimeSpan DrainTimeout { get; init; }

    /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (!IPAddress.TryParse(ListenAddress, out _))
        {
            errors.Add($"{nameof(ListenAddress)} no es una dirección IP ({ListenAddress}).");
        }

        if (Port is < 1 or > 65535)
        {
            errors.Add($"{nameof(Port)} fuera de rango ({Port}).");
        }

        RequirePositive(ProtocolVersion, nameof(ProtocolVersion));
        RequirePositive(MaxInboundMessageBytes, nameof(MaxInboundMessageBytes));
        RequirePositive(MaxPendingConnections, nameof(MaxPendingConnections));
        RequirePositive(CommandsPerSecond, nameof(CommandsPerSecond));
        RequirePositive(MaxViolations, nameof(MaxViolations));

        if (CommandBurst < CommandsPerSecond)
        {
            errors.Add($"{nameof(CommandBurst)} ({CommandBurst}) debe ser mayor o igual que {nameof(CommandsPerSecond)} ({CommandsPerSecond}).");
        }

        if (OutboundQueueCapacity < 2)
        {
            errors.Add($"{nameof(OutboundQueueCapacity)} debe admitir al menos un esencial y una posición ({OutboundQueueCapacity}).");
        }

        RequirePositive(HandshakeTimeout, nameof(HandshakeTimeout));
        RequirePositive(ViolationWindow, nameof(ViolationWindow));
        RequirePositive(PingIdleInterval, nameof(PingIdleInterval));
        RequirePositive(PongTimeout, nameof(PongTimeout));
        RequirePositive(CloseLinger, nameof(CloseLinger));
        RequirePositive(DrainTimeout, nameof(DrainTimeout));

        return errors;

        void RequirePositive<T>(T value, string name)
            where T : IComparable<T>
        {
            if (value.CompareTo(default!) <= 0)
            {
                errors.Add($"{name} debe ser positivo ({value}).");
            }
        }
    }

    /// <summary>Valida la configuración completa.</summary>
    /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
    public void Validate()
    {
        IReadOnlyList<string> errors = GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new ArgumentException("Configuración de Networking inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }
}
