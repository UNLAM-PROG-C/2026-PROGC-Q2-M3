using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Resources;
using Rts.Shared.Time;

namespace Rts.Shared.World;

/// <summary>
/// Valores estáticos de la partida que Godot necesita para su interfaz (DECIDIDO D-068): HP máximo, huellas, costos, duraciones,
/// rangos y velocidad. Es público e igual para ambos jugadores. Lo arma la composición del servidor desde las configuraciones ya
/// validadas de cada módulo (RECOMENDADO D-071) y viaja en el InitialState. No es un DTO JSON.
/// <para>El constructor exige exactamente una entrada por cada <see cref="UnitType"/> y por cada <see cref="BuildingType"/>.</para>
/// </summary>
public sealed record GameCatalog
{
  public GameCatalog(
      TickRate tickRate,
      EquatableArray<UnitCatalogEntry> units,
      EquatableArray<BuildingCatalogEntry> buildings,
      ResourceAmount farmRepairCost,
      int farmRepairWorkRequired,
      double gatherRange,
      double buildRange)
  {
    ArgumentNullException.ThrowIfNull(tickRate);
    RequireExactlyOnce(units.Select(u => u.UnitType), nameof(units));
    RequireExactlyOnce(buildings.Select(b => b.BuildingType), nameof(buildings));
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(farmRepairWorkRequired);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gatherRange);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buildRange);

    TickRate = tickRate;
    Units = units;
    Buildings = buildings;
    FarmRepairCost = farmRepairCost;
    FarmRepairWorkRequired = farmRepairWorkRequired;
    GatherRange = gatherRange;
    BuildRange = buildRange;
  }

  /// <summary>Frecuencia de Simulation: Godot la usa para convertir <c>RemainingTicks</c> en tiempo.</summary>
  public TickRate TickRate { get; }

  public EquatableArray<UnitCatalogEntry> Units { get; }

  public EquatableArray<BuildingCatalogEntry> Buildings { get; }

  public ResourceAmount FarmRepairCost { get; }

  public int FarmRepairWorkRequired { get; }

  public double GatherRange { get; }

  public double BuildRange { get; }

  private static void RequireExactlyOnce<T>(IEnumerable<T> present, string parameter)
      where T : struct, Enum
  {
    T[] values = present.ToArray();
    if (values.Length != Enum.GetValues<T>().Length || values.Distinct().Count() != values.Length || values.Any(v => !Enum.IsDefined(v)))
    {
      throw new ArgumentException($"Se requiere exactamente una entrada por cada {typeof(T).Name}.", parameter);
    }
  }
}

/// <summary>Valores de un tipo de unidad. <see cref="Attack"/> solo si la unidad ataca (el soldado).</summary>
public sealed record UnitCatalogEntry(
    UnitType UnitType,
    int MaxHp,
    ResourceAmount Cost,
    int TrainingDurationTicks,
    double SpeedPerSecond,
    AttackStatsView? Attack);

/// <summary>
/// Valores de un tipo de edificio. <see cref="Cost"/> es nulo para el Centro Principal, que no se construye.
/// <see cref="Attack"/> solo para la torre (su rango se mide desde el borde de la huella, D-019).
/// </summary>
public sealed record BuildingCatalogEntry(
    BuildingType BuildingType,
    int MaxHp,
    FootprintSize Footprint,
    ResourceAmount? Cost,
    AttackStatsView? Attack);

/// <summary>Daño, rango y recarga de un atacante.</summary>
public sealed record AttackStatsView(int Damage, double Range, int CooldownTicks);
