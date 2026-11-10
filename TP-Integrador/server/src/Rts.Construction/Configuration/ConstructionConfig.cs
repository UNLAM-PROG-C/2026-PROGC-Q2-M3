using Rts.Shared.Entities;
using Rts.Shared.Geometry;

namespace Rts.Construction.Configuration;

/// <summary>
/// Parámetros de construcción tipados (03 §10). Construction es dueño del HP máximo de edificios (D-021), de las huellas
/// y del trabajo de obra y reparación. Los costos son de Economy (D-015) y los rangos de interacción, de Movement.
/// Valores PENDIENTES de balance. Se valida una vez al iniciar el servidor.
/// </summary>
public sealed record ConstructionConfig
{
  /// <summary>HP máximo por tipo de edificio (todos los tipos).</summary>
  public required IReadOnlyDictionary<BuildingType, int> BuildingMaxHp { get; init; }

  /// <summary>Tamaño de huella por tipo de edificio (todos los tipos). La valla debe ser 1×1 (03 §1).</summary>
  public required IReadOnlyDictionary<BuildingType, FootprintSize> FootprintSizes { get; init; }

  /// <summary>Fracción del HP máximo con la que nace una obra, en el intervalo abierto (0, 1) (D-035).</summary>
  public required double InitialHpFraction { get; init; }

  /// <summary>HP de obra que aporta cada trabajador en rango, por tick.</summary>
  public required int BuildHpPerWorkerTick { get; init; }

  /// <summary>Trabajo total para completar la reparación de capacidad de una granja.</summary>
  public required int RepairWorkRequired { get; init; }

  /// <summary>Trabajo de reparación que aporta cada trabajador en rango, por tick.</summary>
  public required int RepairWorkPerWorkerTick { get; init; }

  /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
  public IReadOnlyList<string> GetValidationErrors()
  {
    var errors = new List<string>();
    BuildingType[] allTypes = Enum.GetValues<BuildingType>();

    if (BuildingMaxHp is null)
    {
      errors.Add($"{nameof(BuildingMaxHp)} es obligatorio.");
    }
    else
    {
      errors.AddRange(allTypes.Where(t => !BuildingMaxHp.ContainsKey(t)).Select(t => $"Falta el HP máximo de {t}."));
      errors.AddRange(BuildingMaxHp.Where(kv => kv.Value <= 1 || !Enum.IsDefined(kv.Key))
          .Select(kv => $"HP máximo inválido para {kv.Key} ({kv.Value}); debe ser al menos 2."));
    }

    if (FootprintSizes is null)
    {
      errors.Add($"{nameof(FootprintSizes)} es obligatorio.");
    }
    else
    {
      errors.AddRange(allTypes.Where(t => !FootprintSizes.ContainsKey(t)).Select(t => $"Falta la huella de {t}."));
      errors.AddRange(FootprintSizes.Where(kv => kv.Value.CellCount <= 0 || !Enum.IsDefined(kv.Key))
          .Select(kv => $"Huella inválida para {kv.Key} ({kv.Value})."));

      if (FootprintSizes.TryGetValue(BuildingType.Fence, out FootprintSize fence) && fence != new FootprintSize(1, 1))
      {
        errors.Add($"La valla debe ocupar una sola celda (1x1), no {fence}.");
      }
    }

    if (!double.IsFinite(InitialHpFraction) || InitialHpFraction <= 0 || InitialHpFraction >= 1)
    {
      errors.Add($"{nameof(InitialHpFraction)} debe estar en (0, 1) ({InitialHpFraction}).");
    }

    RequirePositive(errors, BuildHpPerWorkerTick, nameof(BuildHpPerWorkerTick));
    RequirePositive(errors, RepairWorkRequired, nameof(RepairWorkRequired));
    RequirePositive(errors, RepairWorkPerWorkerTick, nameof(RepairWorkPerWorkerTick));
    return errors;
  }

  /// <summary>Valida la configuración completa.</summary>
  /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
  public void Validate()
  {
    IReadOnlyList<string> errors = GetValidationErrors();
    if (errors.Count > 0)
    {
      throw new ArgumentException("Configuración de Construction inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
  }

  private static void RequirePositive(List<string> errors, int value, string name)
  {
    if (value <= 0)
    {
      errors.Add($"{name} debe ser positivo ({value}).");
    }
  }
}
