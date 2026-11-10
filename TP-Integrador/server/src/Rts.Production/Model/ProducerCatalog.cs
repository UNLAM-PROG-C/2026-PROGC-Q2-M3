using Rts.Shared.Entities;

namespace Rts.Production.Model;

/// <summary>
/// Qué entrena cada edificio. DECIDIDO (05 §1): el Centro Principal entrena trabajadores y el Cuartel entrena soldados.
/// </summary>
public static class ProducerCatalog
{
  /// <summary>Tipo de unidad que entrena el edificio, o <c>null</c> si no es productor.</summary>
  public static UnitType? GetProducedUnit(BuildingType buildingType) => buildingType switch
  {
    BuildingType.MainCenter => UnitType.Worker,
    BuildingType.Barracks => UnitType.Soldier,
    _ => null,
  };

  public static bool IsProducer(BuildingType buildingType) => GetProducedUnit(buildingType) is not null;
}
