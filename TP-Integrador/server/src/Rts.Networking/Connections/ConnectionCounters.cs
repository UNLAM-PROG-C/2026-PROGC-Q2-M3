namespace Rts.Networking.Connections;

/// <summary>
/// Contadores de una conexión para sus métricas (contrato 08 §11, <see cref="ConnectionMetricsSnapshot"/>). Los escriben el receptor,
/// el escritor y quien encola, y los lee cualquier hilo que pida métricas: por eso todo es <see cref="Interlocked"/>, sin locks.
/// </summary>
internal sealed class ConnectionCounters
{
  private long _messagesReceived;
  private long _bytesReceived;
  private long _messagesSent;
  private long _bytesSent;
  private long _commandsAdmitted;
  private long _commandsRejected;
  private long _positionsSuperseded;

  /// <summary>Líneas completas recibidas, válidas o no.</summary>
  public long MessagesReceived => Interlocked.Read(ref _messagesReceived);

  /// <summary>Bytes leídos del socket.</summary>
  public long BytesReceived => Interlocked.Read(ref _bytesReceived);

  /// <summary>Mensajes escritos en el socket.</summary>
  public long MessagesSent => Interlocked.Read(ref _messagesSent);

  /// <summary>Bytes escritos en el socket, con el <c>\n</c> de cada línea.</summary>
  public long BytesSent => Interlocked.Read(ref _bytesSent);

  /// <summary>Comandos de gameplay que entraron a la cola global.</summary>
  public long CommandsAdmitted => Interlocked.Read(ref _commandsAdmitted);

  /// <summary>Comandos de gameplay respondidos con <c>CommandRejected</c> (estructura inválida o rechazo de la admisión).</summary>
  public long CommandsRejected => Interlocked.Read(ref _commandsRejected);

  /// <summary><c>PositionUpdate</c> pendientes que se quitaron porque llegó uno nuevo (D-064).</summary>
  public long PositionsSuperseded => Interlocked.Read(ref _positionsSuperseded);

  public void AddBytesReceived(int bytes) => Interlocked.Add(ref _bytesReceived, bytes);

  public void CountMessageReceived() => Interlocked.Increment(ref _messagesReceived);

  public void CountMessageSent(int bytes)
  {
    Interlocked.Increment(ref _messagesSent);
    Interlocked.Add(ref _bytesSent, bytes);
  }

  public void CountCommandAdmitted() => Interlocked.Increment(ref _commandsAdmitted);

  public void CountCommandRejected() => Interlocked.Increment(ref _commandsRejected);

  public void CountPositionSuperseded() => Interlocked.Increment(ref _positionsSuperseded);
}
