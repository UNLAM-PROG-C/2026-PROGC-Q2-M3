using Rts.Networking.Connections;
using Rts.Protocol.Outbound;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking.Tests.Connections;

// Piezas falsas para probar las conexiones sin GameSession. Se usan desde varios hilos, por eso protegen sus listas.

internal sealed class FakeSessionEventSink : ISessionEventSink
{
  private readonly object _lock = new();
  private readonly List<ConnectionId> _hellos = [];
  private readonly List<(ConnectionId Connection, InitializationId Initialization)> _readies = [];
  private readonly List<ConnectionId> _closed = [];

  /// <summary>Lo que devuelve <see cref="TryPostHello"/>; falso simula la cola de sesión llena.</summary>
  public bool AcceptHello { get; set; } = true;

  /// <summary>Lo que devuelve <see cref="TryPostClientReady"/>.</summary>
  public bool AcceptReady { get; set; } = true;

  public IReadOnlyList<ConnectionId> Hellos
  {
    get
    {
      lock (_lock)
      {
        return [.. _hellos];
      }
    }
  }

  public IReadOnlyList<(ConnectionId Connection, InitializationId Initialization)> Readies
  {
    get
    {
      lock (_lock)
      {
        return [.. _readies];
      }
    }
  }

  public IReadOnlyList<ConnectionId> Closed
  {
    get
    {
      lock (_lock)
      {
        return [.. _closed];
      }
    }
  }

  public bool TryPostHello(ConnectionId connection)
  {
    lock (_lock)
    {
      _hellos.Add(connection);
    }

    return AcceptHello;
  }

  public bool TryPostClientReady(ConnectionId connection, InitializationId initialization)
  {
    lock (_lock)
    {
      _readies.Add((connection, initialization));
    }

    return AcceptReady;
  }

  public void PostConnectionClosed(ConnectionId connection)
  {
    lock (_lock)
    {
      _closed.Add(connection);
    }
  }
}

/// <summary>Salida falsa de una conexión: anota, en orden, cada mensaje enviado (<see cref="OutboundMessage"/>) y cada cierre (<see cref="ErrorCode"/>).</summary>
internal sealed class FakeConnectionOutput : IConnectionOutput
{
  private readonly object _lock = new();
  private readonly List<object> _actions = [];

  public IReadOnlyList<object> Actions
  {
    get
    {
      lock (_lock)
      {
        return [.. _actions];
      }
    }
  }

  public IReadOnlyList<OutboundMessage> Sent => [.. Actions.OfType<OutboundMessage>()];

  public IReadOnlyList<ErrorCode> Closes => [.. Actions.OfType<ErrorCode>()];

  public void Send(OutboundMessage message)
  {
    lock (_lock)
    {
      _actions.Add(message);
    }
  }

  public void Close(ErrorCode reason)
  {
    lock (_lock)
    {
      _actions.Add(reason);
    }
  }
}
