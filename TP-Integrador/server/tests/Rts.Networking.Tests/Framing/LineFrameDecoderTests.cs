using System.Text;
using Rts.Networking.Framing;

namespace Rts.Networking.Tests.Framing;

public class LineFrameDecoderTests
{
  /// <summary>Máximo holgado para las pruebas que no tratan el tamaño.</summary>
  private const int DefaultMaxLineBytes = 1024;

  /// <summary>Máximo chico para las pruebas de <c>Oversize</c>, así los textos son cortos.</summary>
  private const int SmallMaxLineBytes = 5;

  [Fact]
  public void ReadNext_OneCompleteLine_ReturnsThatLine()
  {
    var decoder = new LineFrameDecoder(DefaultMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes("hola\n"));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, result.Status);
    Assert.Equal("hola", Encoding.UTF8.GetString(result.Frame.Span));
  }

  [Fact]
  public void ReadNext_LineSplitAcrossTwoReads_WaitsAndThenReturnsTheLine()
  {
    var decoder = new LineFrameDecoder(DefaultMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes("ho"));
    FrameReadResult first = decoder.ReadNext();

    decoder.Append(Encoding.UTF8.GetBytes("la\n"));
    FrameReadResult second = decoder.ReadNext();

    Assert.Equal(FrameStatus.NeedMoreData, first.Status);
    Assert.Equal(FrameStatus.Frame, second.Status);
    Assert.Equal("hola", Encoding.UTF8.GetString(second.Frame.Span));
  }

  [Fact]
  public void ReadNext_TwoLinesInOneRead_ReturnsEachLineInOrder()
  {
    var decoder = new LineFrameDecoder(DefaultMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes("uno\ndos\n"));
    FrameReadResult first = decoder.ReadNext();
    string firstText = Encoding.UTF8.GetString(first.Frame.Span);
    FrameReadResult second = decoder.ReadNext();
    string secondText = Encoding.UTF8.GetString(second.Frame.Span);
    FrameReadResult third = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, first.Status);
    Assert.Equal("uno", firstText);
    Assert.Equal(FrameStatus.Frame, second.Status);
    Assert.Equal("dos", secondText);
    Assert.Equal(FrameStatus.NeedMoreData, third.Status);
  }

  [Fact]
  public void ReadNext_LineEndingInCarriageReturnAndNewline_DropsTheCarriageReturn()
  {
    var decoder = new LineFrameDecoder(DefaultMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes("hola\r\n"));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, result.Status);
    Assert.Equal("hola", Encoding.UTF8.GetString(result.Frame.Span));
  }

  [Fact]
  public void ReadNext_EmptyLines_AreSkipped()
  {
    var decoder = new LineFrameDecoder(DefaultMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes("\n\r\nhola\n"));
    FrameReadResult first = decoder.ReadNext();
    string firstText = Encoding.UTF8.GetString(first.Frame.Span);
    FrameReadResult second = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, first.Status);
    Assert.Equal("hola", firstText);
    Assert.Equal(FrameStatus.NeedMoreData, second.Status);
  }

  [Fact]
  public void ReadNext_LineLongerThanMaxWithNewline_ReturnsOversize()
  {
    var decoder = new LineFrameDecoder(SmallMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes(new string('a', SmallMaxLineBytes + 1) + "\n"));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Oversize, result.Status);
  }

  [Fact]
  public void ReadNext_LineLongerThanMaxWithoutNewline_ReturnsOversizeWithoutWaiting()
  {
    var decoder = new LineFrameDecoder(SmallMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes(new string('a', SmallMaxLineBytes + 1)));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Oversize, result.Status);
  }

  [Fact]
  public void ReadNext_LineOfExactlyMaxBytes_ReturnsTheLine()
  {
    var decoder = new LineFrameDecoder(SmallMaxLineBytes);
    string text = new('a', SmallMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes(text + "\n"));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, result.Status);
    Assert.Equal(text, Encoding.UTF8.GetString(result.Frame.Span));
  }

  [Fact]
  public void ReadNext_ExactlyMaxBytesPendingWithoutNewline_WaitsForMore()
  {
    var decoder = new LineFrameDecoder(SmallMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes(new string('a', SmallMaxLineBytes)));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.NeedMoreData, result.Status);
  }

  [Fact]
  public void ReadNext_MaxDoesNotCountTheCarriageReturnOfTheDelimiter()
  {
    var decoder = new LineFrameDecoder(SmallMaxLineBytes);
    string text = new('a', SmallMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes(text + "\r\n"));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, result.Status);
    Assert.Equal(text, Encoding.UTF8.GetString(result.Frame.Span));
  }

  [Fact]
  public void ReadNext_AfterOversize_KeepsReturningOversize()
  {
    var decoder = new LineFrameDecoder(SmallMaxLineBytes);

    decoder.Append(Encoding.UTF8.GetBytes(new string('a', SmallMaxLineBytes + 1) + "\n"));
    decoder.ReadNext();
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Oversize, result.Status);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Constructor_NonPositiveMax_Throws(int maxLineBytes)
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new LineFrameDecoder(maxLineBytes));
  }

  [Fact]
  public void Append_LineLargerThanInitialBuffer_GrowsAndReturnsTheLine()
  {
    int lineBytes = LineFrameDecoder.InitialBufferSize * 3;
    var decoder = new LineFrameDecoder(lineBytes);
    string text = new('a', lineBytes);

    decoder.Append(Encoding.UTF8.GetBytes(text + "\n"));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, result.Status);
    Assert.Equal(text, Encoding.UTF8.GetString(result.Frame.Span));
    Assert.True(decoder.Capacity > LineFrameDecoder.InitialBufferSize);
  }

  [Fact]
  public void Append_ManyConsumedLines_ReusesTheBufferWithoutGrowing()
  {
    const int Lines = 1000;
    var decoder = new LineFrameDecoder(DefaultMaxLineBytes);

    for (int i = 0; i < Lines; i++)
    {
      decoder.Append(Encoding.UTF8.GetBytes("hola\n"));
      FrameReadResult result = decoder.ReadNext();

      Assert.Equal(FrameStatus.Frame, result.Status);
      Assert.Equal("hola", Encoding.UTF8.GetString(result.Frame.Span));
    }

    Assert.Equal(LineFrameDecoder.InitialBufferSize, decoder.Capacity);
  }

  [Fact]
  public void Append_PendingPartialLineAfterConsumedOnes_CompactsInsteadOfGrowing()
  {
    var decoder = new LineFrameDecoder(DefaultMaxLineBytes);
    string first = new('a', LineFrameDecoder.InitialBufferSize - 10);

    // Una línea completa y el comienzo de otra ("bb"): al consumir la primera queda espacio libre adelante.
    decoder.Append(Encoding.UTF8.GetBytes(first + "\nbb"));
    decoder.ReadNext();

    // No entra al final del cajón, pero sí si se mueve "bb" al principio.
    string rest = new('c', 19);
    decoder.Append(Encoding.UTF8.GetBytes(rest + "\n"));
    FrameReadResult result = decoder.ReadNext();

    Assert.Equal(FrameStatus.Frame, result.Status);
    Assert.Equal("bb" + rest, Encoding.UTF8.GetString(result.Frame.Span));
    Assert.Equal(LineFrameDecoder.InitialBufferSize, decoder.Capacity);
  }
}
