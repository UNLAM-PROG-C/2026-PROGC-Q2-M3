namespace Rts.Networking.Framing;

/// <summary>Estado de una lectura del decodificador.</summary>
public enum FrameStatus
{
    /// <summary>No hay una línea completa: hay que leer más bytes del socket.</summary>
    NeedMoreData = 1,

    /// <summary>Hay una línea completa en <see cref="FrameReadResult.Frame"/>.</summary>
    Frame = 2,

    /// <summary>La línea pendiente ya supera el máximo, aunque todavía no llegó su delimitador: se cierra la conexión (D-074).</summary>
    Oversize = 3,
}

/// <summary>Resultado de <see cref="ILineFrameDecoder.ReadNext"/>. <see cref="Frame"/> solo tiene datos si el estado es <c>Frame</c>.</summary>
public readonly record struct FrameReadResult(FrameStatus Status, ReadOnlyMemory<byte> Frame)
{
    public static FrameReadResult NeedMoreData => new(FrameStatus.NeedMoreData, ReadOnlyMemory<byte>.Empty);

    public static FrameReadResult Oversize => new(FrameStatus.Oversize, ReadOnlyMemory<byte>.Empty);
}

/// <summary>
/// Reconstruye líneas desde el flujo de bytes de TCP (08 §2), sin asumir que una lectura es un mensaje: una lectura puede traer un
/// fragmento, varias líneas o partes de dos. Uno por conexión; lo usa solo su receptor, así que no necesita ser seguro para hilos.
/// <list type="bullet">
/// <item>El delimitador es <c>\n</c>. Un <c>\r</c> final se descarta, para tolerar clientes que envían <c>\r\n</c>.</item>
/// <item>Las líneas vacías se ignoran: no son mensajes ni faltas.</item>
/// <item>El máximo se mide en <b>bytes</b>, sin el delimitador. Se detecta en cuanto la línea pendiente lo supera, sin esperar el
/// <c>\n</c>, así que nunca se acumula más que el máximo.</item>
/// <item>No interpreta UTF-8 ni JSON: eso lo hace el parser.</item>
/// </list>
/// </summary>
public interface ILineFrameDecoder
{
    /// <summary>Agrega los bytes recién leídos del socket.</summary>
    void Append(ReadOnlySpan<byte> data);

    /// <summary>
    /// Devuelve la próxima línea completa, si la hay. Se llama en bucle hasta obtener <c>NeedMoreData</c>. Después de <c>Oversize</c>
    /// el decodificador queda inutilizable. La memoria de un <c>Frame</c> es válida hasta la próxima llamada a <see cref="Append"/>.
    /// </summary>
    FrameReadResult ReadNext();
}
