using Rts.Integration.Tests.Support;
using Rts.Measurement;
using Rts.Server.Configuration;

namespace Rts.Integration.Tests;

/// <summary>
/// La herramienta de medición (D-101) con una carga chica: no mide nada en serio (eso se hace con <c>dotnet run -c Release</c>),
/// solo verifica que siga funcionando con los módulos reales y que los dos modos den lo mismo.
/// </summary>
public class MeasurementToolTests
{
  private const int SmallLoad = 30;
  private static readonly MeasurementWindow ShortWindow = new(WarmupTicks: 2, MeasuredTicks: 5);

  [Fact]
  public void SmallLoad_RunsBothModesWithIdenticalResultsAndBuildsTheReport()
  {
    ServerConfig config = IntegrationMatch.LoadConfig();

    LoadMeasurement result = ParallelismMeasurement.Measure(config, SmallLoad, ShortWindow);
    string markdown = MeasurementReport.ToMarkdown([result], ShortWindow, DateTimeOffset.UnixEpoch);

    Assert.True(result.IdenticalResults);
    Assert.True(result.LoadStayedConstant);
    Assert.Equal(ShortWindow.MeasuredTicks, result.MeasuredTicks);
    Assert.Equal(ShortWindow.MeasuredTicks, result.Sequential.Summary.TickCount);
    Assert.Contains($"| {SmallLoad} |", markdown, StringComparison.Ordinal);
  }
}
