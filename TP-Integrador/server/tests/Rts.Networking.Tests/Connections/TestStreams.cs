using System.Text;
using System.Threading.Channels;

namespace Rts.Networking.Tests.Connections;

/// <summary>
/// Stream falso de solo lectura al que la prueba le va dando bytes mientras la conexión corre. <see cref="Complete"/> simula que el
/// cliente cerró su lado; <see cref="Fail"/>, que el socket falló.
/// </summary>
internal sealed class LiveReadStream : Stream
{
  private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
  private byte[] _pending = [];

  public void Feed(string text) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

  public void Complete() => _chunks.Writer.TryComplete();

  public void Fail(Exception exception) => _chunks.Writer.TryComplete(exception);

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
    if (_pending.Length == 0)
    {
      if (!await _chunks.Reader.WaitToReadAsync(cancellationToken))
      {
        return 0; // El cliente cerró su lado.
      }

      _pending = await _chunks.Reader.ReadAsync(cancellationToken);
    }

    int count = Math.Min(_pending.Length, buffer.Length);
    _pending.AsMemory(0, count).CopyTo(buffer);
    _pending = _pending[count..];
    return count;
  }

  public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Se lee de forma asincrónica.");

  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

  public override void Flush()
  {
  }

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>Une un stream de lectura con uno de escritura, como un socket: lo que la conexión lee y lo que escribe son canales separados.</summary>
internal sealed class CompositeStream(Stream reader, Stream writer) : Stream
{
  public override bool CanRead => true;

  public override bool CanSeek => false;

  public override bool CanWrite => true;

  public override long Length => throw new NotSupportedException();

  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
      reader.ReadAsync(buffer, cancellationToken);

  public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
      writer.WriteAsync(buffer, cancellationToken);

  public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Se lee de forma asincrónica.");

  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Se escribe de forma asincrónica.");

  public override void Flush()
  {
  }

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();
}
