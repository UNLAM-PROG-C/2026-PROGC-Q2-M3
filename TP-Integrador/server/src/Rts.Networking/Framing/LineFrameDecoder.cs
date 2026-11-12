namespace Rts.Networking.Framing;

/// <summary>
/// Implementación de <see cref="ILineFrameDecoder"/>: acumula los bytes en un cajón y entrega una línea por vez.
/// <list type="bullet">
/// <item><c>_start</c> marca dónde empieza lo que todavía no se entregó y <c>_length</c> dónde terminan los bytes guardados.</item>
/// <item>El cajón se reacomoda (se compacta o crece) solo en <see cref="Append"/>, como pide la interfaz.</item>
/// <item>El máximo no cuenta el delimitador: un <c>\r</c> justo antes del <c>\n</c> es parte del delimitador.</item>
/// <item>Después de <c>Oversize</c> no se recupera: todo <see cref="ReadNext"/> posterior devuelve <c>Oversize</c>.</item>
/// </list>
/// Una instancia por conexión; no es segura para hilos. Se espera que <see cref="Append"/> reciba lecturas de tamaño acotado
/// (las del socket), no mensajes enteros arbitrariamente grandes.
/// </summary>
internal sealed class LineFrameDecoder : ILineFrameDecoder
{
  /// <summary>Tamaño inicial del cajón, en bytes. Suposición temporal: no es un valor decidido por el equipo.</summary>
  internal const int InitialBufferSize = 256;

  private readonly int _maxLineBytes;
  private byte[] _buffer = new byte[InitialBufferSize];
  private int _start;
  private int _length;
  private bool _isOversize;

  public LineFrameDecoder(int maxLineBytes)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineBytes);
    _maxLineBytes = maxLineBytes;
  }

  /// <summary>Tamaño actual del cajón, en bytes. Solo para pruebas y métricas.</summary>
  internal int Capacity => _buffer.Length;

  /// <inheritdoc/>
  public void Append(ReadOnlySpan<byte> data)
  {
    MakeRoomFor(data.Length);
    data.CopyTo(_buffer.AsSpan(_length));
    _length += data.Length;
  }

  /// <summary>
  /// Deja lugar para <paramref name="extra"/> bytes al final del cajón. Es el único lugar donde los bytes cambian de posición,
  /// y por eso la memoria de un <c>Frame</c> solo vale hasta el próximo <see cref="Append"/>.
  /// </summary>
  private void MakeRoomFor(int extra)
  {
    // Si ya se entregó todo, se vuelve al principio sin copiar nada.
    if (_start == _length)
    {
      _start = 0;
      _length = 0;
    }

    if (_buffer.Length - _length >= extra)
    {
      return;
    }

    // No hay lugar al final: si lo pendiente más lo nuevo entra en el cajón actual se compacta, si no se agranda.
    int pending = _length - _start;
    byte[] target = pending + extra <= _buffer.Length
      ? _buffer
      : new byte[Math.Max(_buffer.Length * 2, pending + extra)];

    _buffer.AsSpan(_start, pending).CopyTo(target);
    _buffer = target;
    _start = 0;
    _length = pending;
  }

  /// <inheritdoc/>
  public FrameReadResult ReadNext()
  {
    if (_isOversize)
    {
      return FrameReadResult.Oversize;
    }

    while (true)
    {
      int pending = _length - _start;
      int newline = _buffer.AsSpan(_start, pending).IndexOf((byte)'\n');

      if (newline < 0)
      {
        return IsPendingTooLong(pending) ? MarkOversize() : FrameReadResult.NeedMoreData;
      }

      int lineLength = newline;

      if (newline > 0 && _buffer[_start + newline - 1] == (byte)'\r')
      {
        lineLength--;
      }

      if (lineLength > _maxLineBytes)
      {
        return MarkOversize();
      }

      ReadOnlyMemory<byte> line = _buffer.AsMemory(_start, lineLength);
      _start += newline + 1;

      if (lineLength > 0)
      {
        return new FrameReadResult(FrameStatus.Frame, line);
      }
    }
  }

  /// <summary>
  /// Con la línea todavía sin <c>\n</c>, decide si ya superó el máximo. Un <c>\r</c> final podría ser parte del delimitador
  /// (<c>\r\n</c>), así que no cuenta hasta saber qué sigue.
  /// </summary>
  private bool IsPendingTooLong(int pending)
  {
    if (pending <= _maxLineBytes)
    {
      return false;
    }

    if (pending > _maxLineBytes + 1)
    {
      return true;
    }

    return _buffer[_length - 1] != (byte)'\r';
  }

  private FrameReadResult MarkOversize()
  {
    _isOversize = true;
    return FrameReadResult.Oversize;
  }
}