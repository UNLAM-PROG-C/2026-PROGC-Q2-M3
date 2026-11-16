using System.Text;

namespace Rts.Networking.Tests.Connections;

/// <summary>
/// Stream falso de solo lectura que entrega los fragmentos que la prueba le da, uno por lectura. Cuando se acaban, devuelve fin de
/// conexión (0 bytes) o, con <see cref="BlockWhenEmpty"/>, espera hasta que se cancele.
/// </summary>
internal sealed class ScriptedStream : Stream
{
  private readonly Queue<byte[]> _chunks = new();

  public bool BlockWhenEmpty { get; set; }

  public ScriptedStream Add(string text) => Add(Encoding.UTF8.GetBytes(text));

  public ScriptedStream Add(byte[] bytes)
  {
    _chunks.Enqueue(bytes);
    return this;
  }

  public override bool CanRead => true;

  public override bool CanSeek => false;

  public override bool CanWrite => false;

  public override long Length => throw new NotSupportedException();

  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    if (_chunks.TryDequeue(out byte[]? chunk))
    {
      int count = Math.Min(chunk.Length, buffer.Length);
      chunk.AsMemory(0, count).CopyTo(buffer);
      if (count < chunk.Length)
      {
        // Lo que no entró en el buffer se entrega en la próxima lectura, antes que el resto.
        var rest = new Queue<byte[]>(_chunks);
        _chunks.Clear();
        _chunks.Enqueue(chunk[count..]);
        foreach (byte[] next in rest)
        {
          _chunks.Enqueue(next);
        }
      }

      return count;
    }

    if (BlockWhenEmpty)
    {
      await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    return 0;
  }

  public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Se lee de forma asincrónica.");

  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

  public override void Flush()
  {
  }

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();
}
