using Rts.Shared.Entities;

namespace Rts.Production.Configuration;

/// <summary>
/// Parámetros de producción tipados (05 §10). Los costos en comida son de Economy (D-015). Valores PENDIENTES de balance.
/// Se valida una vez al iniciar el servidor.
/// </summary>
public sealed record ProductionConfig
{
  /// <summary>Duración de entrenamiento por tipo de unidad, en ticks.</summary>
  public required IReadOnlyDictionary<UnitType, int> TrainingDurationTicks { get; init; }

  /// <summary>Máximo de solicitudes pendientes por productor, sin contar la tarea activa (05 §2).</summary>
  public required int MaxPendingPerProducer { get; init; }

  /// <summary>Radio de búsqueda de aparición: cantidad de anillos de celdas alrededor de la huella (05 §5).</summary>
  public required int SpawnSearchRadiusCells { get; init; }

  /// <summary>Devuelve la lista de problemas de configuración; vacía si es válida.</summary>
  public IReadOnlyList<string> GetValidationErrors()
  {
    var errors = new List<string>();

    if (TrainingDurationTicks is null)
    {
      errors.Add($"{nameof(TrainingDurationTicks)} es obligatorio.");
    }
    else
    {
      errors.AddRange(Enum.GetValues<UnitType>().Where(t => !TrainingDurationTicks.ContainsKey(t))
          .Select(t => $"Falta la duración de entrenamiento de {t}."));
      errors.AddRange(TrainingDurationTicks.Where(kv => kv.Value <= 0 || !Enum.IsDefined(kv.Key))
          .Select(kv => $"Duración inválida para {kv.Key} ({kv.Value})."));
    }

    if (MaxPendingPerProducer <= 0)
    {
      errors.Add($"{nameof(MaxPendingPerProducer)} debe ser positivo ({MaxPendingPerProducer}).");
    }

    if (SpawnSearchRadiusCells <= 0)
    {
      errors.Add($"{nameof(SpawnSearchRadiusCells)} debe ser positivo ({SpawnSearchRadiusCells}).");
    }

    return errors;
  }

  /// <summary>Valida la configuración completa.</summary>
  /// <exception cref="ArgumentException">Con todos los problemas encontrados.</exception>
  public void Validate()
  {
    IReadOnlyList<string> errors = GetValidationErrors();
    if (errors.Count > 0)
    {
      throw new ArgumentException("Configuración de Production inválida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
  }
}
