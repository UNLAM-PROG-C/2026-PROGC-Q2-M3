using Rts.Combat.Configuration;
using Rts.Combat.Contracts;
using Rts.Combat.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Combat.Tests;

/// <summary>
/// Datos de prueba de la implementación de Combat. Rangos, daños y cooldowns se derivan de
/// <see cref="CombatConfigTests.Valid"/> para que las pruebas no dependan de números sueltos.
/// </summary>
internal static class CombatTestData
{
  /// <summary>Margen para quedar apenas fuera de rango.</summary>
  public const double JustOutside = 0.01;

  public static readonly CombatConfig Config = CombatConfigTests.Valid();
  public static readonly GridSpec Grid = new(cellSize: 1.0, columns: 50, rows: 50);
  public static readonly TickId Now = new(500);

  public static readonly EntityId Soldier1 = new(1);
  public static readonly EntityId Soldier2 = new(2);
  public static readonly EntityId Unit3 = new(3);
  public static readonly EntityId Unit4 = new(4);
  public static readonly EntityId Unit9 = new(9);
  public static readonly EntityId Tower20 = new(20);
  public static readonly EntityId Building30 = new(30);

  /// <summary>Huella 2×2 anclada en (10, 10): en el mundo ocupa X y Z entre 10 y 12. Su borde derecho está en X = 12.</summary>
  public static readonly GridCell Anchor = new(10, 10);
  public static readonly FootprintSize TwoByTwo = new(2, 2);
  public const double RightBorderX = 12.0;
  public const double MiddleZ = 11.0;

  /// <summary>Celda libre al lado de la huella de <see cref="Anchor"/>.</summary>
  public static readonly GridCell NextToAnchor = new(13, 10);

  /// <summary>Un punto cualquiera del mapa, lejos de las huellas.</summary>
  public static readonly PositionXZ Field = new(5, 5);

  /// <summary>HP suficiente para que un edificio no caiga en las pruebas que no tratan de destrucción.</summary>
  public const int SturdyHp = 1_000;

  public static double SoldierRange => Config.Soldier.Range;

  public static double TowerRange => Config.Tower.Range;

  public static CombatUnit Soldier(EntityId id, PlayerId owner, PositionXZ position, int? hp = null) =>
      new(id, owner, UnitType.Soldier, position, hp ?? Config.UnitMaxHp[UnitType.Soldier]);

  public static CombatUnit Worker(EntityId id, PlayerId owner, PositionXZ position, int? hp = null) =>
      new(id, owner, UnitType.Worker, position, hp ?? Config.UnitMaxHp[UnitType.Worker]);

  public static CombatBuilding Building(EntityId id, PlayerId owner, BuildingType type, int hp, TickId? operationalFrom) =>
      new(id, owner, type, new Footprint(Anchor, TwoByTwo), hp, operationalFrom);

  /// <summary>Torre completada y operativa desde antes de <see cref="Now"/>.</summary>
  public static CombatBuilding OperationalTower(PlayerId owner, int hp) =>
      Building(Tower20, owner, BuildingType.Tower, hp, operationalFrom: TickId.Zero);

  /// <summary>Orden Attack de un soldado; por defecto listo (un atacante nuevo arranca listo, D-022).</summary>
  public static SoldierAttacker Attack(EntityId soldier, EntityId target, TickId? readyAt = null) =>
      new(soldier, target, readyAt ?? TickId.Zero);

  public static TowerAttacker ReadyTower(EntityId tower) => new(tower, TickId.Zero);

  public static PositionXZ At(double x, double z) => new(x, z);

  public static CombatTickInput Input(
      EquatableArray<CombatUnit> units = default,
      EquatableArray<CombatBuilding> buildings = default,
      EquatableArray<SoldierAttacker> soldiers = default,
      EquatableArray<TowerAttacker> towers = default,
      TickId? tick = null) => new(tick ?? Now, Grid, units, buildings, soldiers, towers);

  public static CombatTickResult Calculate(CombatTickInput input) => new CombatCalculator(Config).CalculateTick(input);
}
