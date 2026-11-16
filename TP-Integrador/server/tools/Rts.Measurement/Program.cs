using Rts.Measurement;
using Rts.Server.Configuration;
using Rts.Server.Validation;

// Medición de Economy ∥ Combat en paralelo frente a secuencial (paso 23, D-101). No levanta el servidor ni la red: arma el motor
// como la composición, corre un barrido de carga real e imprime la tabla. Uso, desde esta carpeta:
//   dotnet run -c Release                 → escribe docs/measurements/parallel-vs-sequential.md
//   dotnet run -c Release -- --output X   → escribe el Markdown en X

const int Success = 0;
const int ResultsDiffer = 1;
const int InvalidSetup = 2;
const string OutputOption = "--output";
const string ConfigFile = "server.example.json";
const string SolutionFile = "Rts.slnx";

ServerConfig config = ServerConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, ConfigFile));
IReadOnlyList<string> errors = ConfigurationValidator.Validate(config);
if (errors.Count > 0)
{
  Console.Error.WriteLine($"La configuración de ejemplo es inválida: {string.Join(" ", errors)}");
  return InvalidSetup;
}

string? outputPath = OutputPathFrom(args) ?? DefaultOutputPath();
if (outputPath is null)
{
  Console.Error.WriteLine($"No encontré la carpeta docs del repo: pasá la ruta con {OutputOption} <archivo.md>.");
  return InvalidSetup;
}

Console.WriteLine($"Compilación: {MeasurementReport.BuildConfiguration} · {Environment.ProcessorCount} núcleos lógicos");
MeasurementWindow window = MeasurementWindow.Default;
var results = new List<LoadMeasurement>();
foreach (int units in ParallelismMeasurement.DefaultLoads)
{
  Console.WriteLine($"Midiendo {units} unidades...");
  results.Add(ParallelismMeasurement.Measure(config, units, window));
}

Console.WriteLine();
Console.WriteLine(MeasurementReport.ToTable(results));
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, MeasurementReport.ToMarkdown(results, window, DateTimeOffset.Now));
Console.WriteLine($"Resultados guardados en {outputPath}");
return results.All(result => result.IdenticalResults) ? Success : ResultsDiffer;

static string? OutputPathFrom(string[] args)
{
  int index = Array.IndexOf(args, OutputOption);
  return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
}

// Sube desde la carpeta del ejecutable hasta la del servidor (la que tiene Rts.slnx) y apunta a TP-Integrador/docs/measurements.
static string? DefaultOutputPath()
{
  for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
  {
    if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
    {
      return Path.Combine(directory.Parent!.FullName, "docs", "measurements", "parallel-vs-sequential.md");
    }
  }

  return null;
}
