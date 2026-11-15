using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Rts.Simulation.Diagnostics;

namespace Rts.Measurement;

/// <summary>Arma la tabla de resultados para la consola y el Markdown para la defensa (D-101).</summary>
public static class MeasurementReport
{
  private const string MsFormat = "F3";
  private const string SpeedupFormat = "F2";
  private const string Identical = "idénticos ✔";
  private const string Different = "DISTINTOS ✘";

  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

  /// <summary>Configuración con que se compiló la herramienta: en Debug los números no sirven.</summary>
  public static string BuildConfiguration =>
#if DEBUG
      "Debug";
#else
      "Release";
#endif

  public static string ToTable(IReadOnlyList<LoadMeasurement> results)
  {
    ArgumentNullException.ThrowIfNull(results);
    var text = new StringBuilder();
    text.AppendLine("Unidades | Secuencial 5+6 (mediana / prom) | Paralelo 5+6 (mediana / prom) | Speedup | Tick (sec / par) | Resultados");
    foreach (LoadMeasurement result in results)
    {
      text.AppendLine(string.Join(" | ", Row(result)));
    }

    return text.ToString();
  }

  public static string ToMarkdown(IReadOnlyList<LoadMeasurement> results, MeasurementWindow window, DateTimeOffset measuredAt)
  {
    ArgumentNullException.ThrowIfNull(results);
    ArgumentNullException.ThrowIfNull(window);
    var text = new StringBuilder();
    AppendHeader(text, window, measuredAt);
    text.AppendLine("| Unidades | Secuencial 5+6 (mediana / prom) | Paralelo 5+6 (mediana / prom) | Speedup | Tick completo (sec / par) | Resultados |");
    text.AppendLine("|---:|---:|---:|---:|---:|:---|");
    foreach (LoadMeasurement result in results)
    {
      text.AppendLine($"| {string.Join(" | ", Row(result))} |");
    }

    AppendHowToRead(text);
    AppendBreakdown(text, results);
    AppendWarnings(text, results);
    return text.ToString();
  }

  /// <summary>
  /// Por qué el speedup es el que es, con promedios: en secuencial la fase 5 es Economy + Combat y la fase 6 es casi solo el commit
  /// (la barrera no espera); en paralelo, fase 5 + espera en la barrera ≈ la rama más lenta. Con eso sale el techo del bloque 5+6.
  /// </summary>
  private static void AppendBreakdown(StringBuilder text, IReadOnlyList<LoadMeasurement> results)
  {
    text.AppendLine();
    text.AppendLine("**Por qué el speedup es ese** (promedios):");
    text.AppendLine();
    text.AppendLine("| Unidades | Economy + Combat (sec, fase 5) | Rama más lenta (par, fase 5 + barrera) | Commit (sec, fase 6) | Techo 5+6 | Speedup (prom) |");
    text.AppendLine("|---:|---:|---:|---:|---:|---:|");
    foreach (LoadMeasurement result in results)
    {
      DomainBreakdown breakdown = DomainBreakdown.From(result);
      double averageSpeedup = result.Sequential.AverageDomainMs / result.Parallel.AverageDomainMs; // Promedio contra promedio.
      text.AppendLine(
          $"| {result.Units.ToString(Invariant)} | {Ms(breakdown.BothBranchesMs)} ms | {Ms(breakdown.SlowestBranchMs)} ms | "
          + $"{Ms(breakdown.CommitMs)} ms | {breakdown.Ceiling.ToString(SpeedupFormat, Invariant)}× | {averageSpeedup.ToString(SpeedupFormat, Invariant)}× |");
    }

    text.AppendLine();
    text.AppendLine("Techo 5+6 = (Economy + Combat + commit) / (rama más lenta + commit): lo máximo que el paralelo podría ganar en este bloque si coordinar no costara nada. Cuanto más desparejas las ramas y más pesado el commit (que es secuencial en los dos modos), más bajo el techo. Es aproximado: la rama más lenta se estima en paralelo, donde también compite con la otra por memoria y caché. Toda esta tabla usa promedios, así que no se compara con el speedup de medianas de arriba.");
  }

  private static IEnumerable<string> Row(LoadMeasurement result) =>
  [
    result.Units.ToString(Invariant), // Sin separador de miles: "1,500" en español se lee como "uno coma cinco".
    $"{Ms(result.Sequential.MedianDomainMs)} / {Ms(result.Sequential.AverageDomainMs)} ms",
    $"{Ms(result.Parallel.MedianDomainMs)} / {Ms(result.Parallel.AverageDomainMs)} ms",
    $"{result.Speedup.ToString(SpeedupFormat, Invariant)}×",
    $"{Ms(result.Sequential.MedianTickMs)} / {Ms(result.Parallel.MedianTickMs)} ms",
    result.IdenticalResults ? Identical : Different,
  ];

  private static string Ms(double milliseconds) => milliseconds.ToString(MsFormat, Invariant);

  /// <summary>Descomposición del bloque 5+6 a partir de los promedios por fase de cada modo.</summary>
  private sealed record DomainBreakdown(double BothBranchesMs, double SlowestBranchMs, double CommitMs)
  {
    public double Ceiling => (BothBranchesMs + CommitMs) / (SlowestBranchMs + CommitMs);

    public static DomainBreakdown From(LoadMeasurement result)
    {
      TickMetricsSummary sequential = result.Sequential.Summary;
      TickMetricsSummary parallel = result.Parallel.Summary;
      double commit = PhaseMs(sequential, SimulationPhase.BarrierAndCommit) - sequential.AverageBarrierWait.TotalMilliseconds;
      double slowest = PhaseMs(parallel, SimulationPhase.EconomyAndCombat) + parallel.AverageBarrierWait.TotalMilliseconds;
      return new DomainBreakdown(PhaseMs(sequential, SimulationPhase.EconomyAndCombat), slowest, commit);
    }

    private static double PhaseMs(TickMetricsSummary summary, SimulationPhase phase) =>
        summary.AveragePhases.Single(timing => timing.Phase == phase).Duration.TotalMilliseconds;
  }

  private static void AppendHeader(StringBuilder text, MeasurementWindow window, DateTimeOffset measuredAt)
  {
    text.AppendLine("# Medición: Economy ∥ Combat en paralelo frente a secuencial");
    text.AppendLine();
    text.AppendLine("> Generado por `server/tools/Rts.Measurement` (D-101). Cada corrida sobrescribe este archivo.");
    text.AppendLine();
    text.AppendLine($"- **Fecha:** {measuredAt.ToString("yyyy-MM-dd HH:mm", Invariant)}");
    text.AppendLine($"- **Máquina:** {Environment.ProcessorCount} núcleos lógicos, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}");
    text.AppendLine($"- **Compilación:** {BuildConfiguration}");
    text.AppendLine($"- **Ventana:** {window.WarmupTicks} ticks de calentamiento (descartados) + {window.MeasuredTicks} medidos por modo, intercalados tick a tick");
    text.AppendLine("- **Escenario:** bloques de [árbol][trabajador][soldado P1][soldado P2], todo en rango; Economy, Combat y Movement reales, configuración de `server.example.json`");
    text.AppendLine();
  }

  private static void AppendHowToRead(StringBuilder text)
  {
    text.AppendLine();
    text.AppendLine("**Cómo leerlo:**");
    text.AppendLine();
    text.AppendLine("- **5+6** = fase 5 (lanzar Economy y Combat) + fase 6 (barrera y commit). Se suman porque en paralelo la fase 5 solo lanza las tareas y la espera queda en la 6.");
    text.AppendLine("- **Speedup** = mediana secuencial / mediana paralela. Mayor que 1: gana el paralelo. El techo teórico es (Economy + Combat) / max(Economy, Combat), como mucho 2×.");
    text.AppendLine("- Con poca carga cada rama tarda microsegundos y el costo de coordinar dos tareas puede superar la ganancia: el paralelo conviene a partir de cierta carga.");
    text.AppendLine("- **Resultados** compara las publicaciones de los dos modos en cada tick (D-051).");
  }

  private static void AppendWarnings(StringBuilder text, IReadOnlyList<LoadMeasurement> results)
  {
    List<string> warnings = [.. results.SelectMany(Warnings)];
    if (BuildConfiguration != "Release")
    {
      warnings.Insert(0, "Compilado en Debug: los tiempos no son representativos. Correr con `dotnet run -c Release`.");
    }

    if (warnings.Count == 0)
    {
      return;
    }

    text.AppendLine();
    text.AppendLine("**Avisos:**");
    text.AppendLine();
    warnings.ForEach(warning => text.AppendLine($"- {warning}"));
  }

  private static IEnumerable<string> Warnings(LoadMeasurement result)
  {
    if (result.WindowShortened)
    {
      yield return $"{result.Units} unidades: la ventana se acortó a {result.MeasuredTicks} ticks para terminar antes de la primera muerte.";
    }

    if (!result.LoadStayedConstant)
    {
      yield return $"{result.Units} unidades: murieron unidades durante la medición; la carga no fue constante.";
    }

    if (!result.IdenticalResults)
    {
      yield return $"{result.Units} unidades: los dos modos publicaron cosas distintas. Eso rompe D-051 y hay que investigarlo.";
    }
  }
}
