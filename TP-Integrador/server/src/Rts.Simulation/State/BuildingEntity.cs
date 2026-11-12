using Rts.Construction.Model;
using Rts.Economy.Model;
using Rts.Production.Model;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;

namespace Rts.Simulation.State;

/// <summary>
/// Edificio en el <see cref="WorldState"/> oficial (inmutable, D-044). Sus partes opcionales dependen del tipo y del estado:
/// <list type="bullet">
/// <item><see cref="FarmEconomy"/>: solo granjas completadas (Economy).</item>
/// <item><see cref="Repair"/>: solo granjas completadas, agotadas y en reparación (Construction, D-012).</item>
/// <item><see cref="Production"/>: solo productores completados: Centro Principal y Cuartel (Production).</item>
/// <item><see cref="TowerReadyAtTick"/>: solo torres completadas (cooldown de Combat).</item>
/// </list>
/// <see cref="OperationalFromTick"/> es nulo mientras está en obra. El constructor verifica estas reglas.
/// </summary>
public sealed record BuildingEntity
{
  public BuildingEntity(
      EntityId id,
      PlayerId owner,
      BuildingType type,
      Footprint footprint,
      int hp,
      TickId? operationalFromTick,
      FarmEconomyState? farmEconomy,
      FarmRepairState? repair,
      ProducerState? production,
      TickId? towerReadyAtTick)
  {
    if (id.IsNone || !owner.IsValid || !Enum.IsDefined(type))
    {
      throw new ArgumentException("Edificio con ID, dueño o tipo inválido.");
    }

    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hp);
    bool completed = operationalFromTick is not null;

    if (type == BuildingType.MainCenter && !completed)
    {
      throw new ArgumentException("El Centro Principal existe completado desde el inicio.", nameof(operationalFromTick));
    }

    bool isCompletedFarm = completed && type == BuildingType.Farm;
    if ((farmEconomy is not null) != isCompletedFarm || (farmEconomy is not null && (farmEconomy.FarmId != id || farmEconomy.Owner != owner)))
    {
      throw new ArgumentException("El estado económico existe exactamente para las granjas completadas y debe corresponderles.", nameof(farmEconomy));
    }

    if (repair is not null && (farmEconomy is not { Status: FarmProductionStatus.Depleted } || repair.FarmId != id || repair.Owner != owner))
    {
      throw new ArgumentException("Solo una granja completada y agotada puede estar en reparación.", nameof(repair));
    }

    bool isCompletedProducer = completed && ProducerCatalog.IsProducer(type);
    if ((production is not null) != isCompletedProducer ||
        (production is not null && (production.ProducerId != id || production.Owner != owner || production.ProducerType != type || production.Footprint != footprint)))
    {
      throw new ArgumentException("El estado de producción existe exactamente para los productores completados y debe corresponderles.", nameof(production));
    }

    if ((towerReadyAtTick is not null) != (completed && type == BuildingType.Tower))
    {
      throw new ArgumentException("El cooldown de torre existe exactamente para las torres completadas.", nameof(towerReadyAtTick));
    }

    Id = id;
    Owner = owner;
    Type = type;
    Footprint = footprint;
    Hp = hp;
    OperationalFromTick = operationalFromTick;
    FarmEconomy = farmEconomy;
    Repair = repair;
    Production = production;
    TowerReadyAtTick = towerReadyAtTick;
  }

  public EntityId Id { get; }

  public PlayerId Owner { get; }

  public BuildingType Type { get; }

  public Footprint Footprint { get; }

  public int Hp { get; }

  public TickId? OperationalFromTick { get; }

  public bool IsCompleted => OperationalFromTick is not null;

  public FarmEconomyState? FarmEconomy { get; }

  public FarmRepairState? Repair { get; }

  public ProducerState? Production { get; }

  public TickId? TowerReadyAtTick { get; }

  /// <summary>Copia con el HP que dejó el daño de Combat. Un edificio completado y dañado sigue completado (02 §4).</summary>
  internal BuildingEntity WithHp(int hp) => new(
      Id, Owner, Type, Footprint, hp, OperationalFromTick, FarmEconomy, Repair, Production, TowerReadyAtTick);

  /// <summary>Copia de la torre con el nuevo cooldown que calculó Combat.</summary>
  internal BuildingEntity WithTowerReadyAt(TickId readyAtTick) => new(
      Id, Owner, Type, Footprint, Hp, OperationalFromTick, FarmEconomy, Repair, Production, readyAtTick);

  /// <summary>Copia de la granja con el estado productivo que confirmó Economy. El constructor verifica que siga siendo coherente.</summary>
  internal BuildingEntity WithFarmEconomy(FarmEconomyState farmEconomy) => new(
      Id, Owner, Type, Footprint, Hp, OperationalFromTick, farmEconomy, Repair, Production, TowerReadyAtTick);
}
