namespace Rts.Networking.Tests.Outbound;

/// <summary>
/// Stream falso de solo escritura que anota cada llamada a <c>WriteAsync</c> como un bloque de bytes. Con <see cref="Gate"/> se
/// simula un cliente lento: cada escritura espera hasta que la puerta se complete.
/// </summary>
internal sealed class RecordingStream : Stream
{
  private readonly object _lock = new();
  private readonly List<byte[]> _writes = [];
  private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

  /// <summary>Si no es <c>null</c>, cada escritura espera a que esta tarea termine antes de anotarse.</summary>
  public Task? Gate { get; set; }

  /// <summary>Si no es <c>null</c>, cada escritura lanza esta excepción (simula un socket que falla).</summary>
  public Exception? WriteException { get; set; }

  /// <summary>Termina cuando entra la primera escritura (todavía sin anotarse si hay una puerta cerrada).</summary>
  public Task WriteStarted => _writeStarted.Task;

  /// <summary>Un bloque por cada <c>WriteAsync</c> terminado, en orden.</summary>
  public IReadOnlyList<byte[]> Writes
  {
    get
    {
      lock (_lock)
      {
        return [.. _writes];
      }
    }
  }

  public override bool CanRead => false;

  public override bool CanSeek => false;

  public override bool CanWrite => true;

  public override long Length => throw new NotSupportedException();

  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
  {
    _writeStarted.TrySetResult();
    if (WriteException is { } failure)
    {
      throw failure;
    }

    if (Gate is { } gate)
    {
      await gate.WaitAsync(cancellationToken);
    }

    cancellationToken.ThrowIfCancellationRequested();
    lock (_lock)
    {
      _writes.Add(buffer.ToArray());
    }
  }

  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("El SendLoop escribe de forma asincrónica.");

  public override void Flush()
  {
  }

  public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();
}
