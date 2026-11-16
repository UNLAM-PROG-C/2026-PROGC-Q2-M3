using Rts.Construction;
using Rts.Construction.Contracts;
using Rts.Construction.Model;
using Rts.Economy;
using Rts.Economy.Contracts;
using Rts.Economy.Model;
using Rts.Movement;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Commands;

/// <summary>
/// <c>RepairFarm</c> (03 §4): un grupo de trabajadores repara la capacidad productiva de una granja agotada. Es el primer handler que
/// <b>cobra</b>, así que decide todo antes de tocar el mundo:
/// <list type="number">
/// <item>Construction (<see cref="IConstructionRules.EvaluateRepairStart"/>): granja propia, completada y agotada. Responde
/// <c>StartAndPay</c> (no hay reparación: hay que pagar) o <c>Join</c> (ya hay una: los trabajadores se suman gratis).</item>
/// <item>Cada trabajador, con <see cref="TaskOrderPlan"/>: propiedad y Movement (ruta en rango de la huella). Si ninguno llega, se
/// rechaza el comando entero, <b>sin cobrar</b> (RECOMENDADO D-036).</item>
/// <item>Solo en <c>StartAndPay</c>: Economy cobra el costo de reparación <b>una vez</b> (<see cref="IEconomyRules.EvaluateSpend"/>).
/// Si no alcanza, se rechaza el comando entero.</item>
/// <item>Confirma todo junto: saldo, reparación en 0 y <see cref="FarmRepairStarted"/>; después, las órdenes de los trabajadores.</item>
/// </list>
/// </summary>
internal sealed class RepairFarmCommandHandler : ICommandHandler
{
  private readonly IConstructionRules _construction;
  private readonly IEconomyRules _economy;
  private readonly IMovementRules _movement;

  public RepairFarmCommandHandler(IConstructionRules construction, IEconomyRules economy, IMovementRules movement)
  {
    _construction = construction ?? throw new ArgumentNullException(nameof(construction));
    _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    _movement = movement ?? throw new ArgumentNullException(nameof(movement));
  }

  public CommandType Type => CommandType.RepairFarm;

  public CommandResult Handle(CommandEnvelope command, TickContext context)
  {
    var repair = (RepairFarmCommand)command.Command;
    WorldState world = context.World;
    world.Buildings.TryGetValue(repair.FarmId, out BuildingEntity? farm);

    RepairStartEvaluation start = _construction.EvaluateRepairStart(command.Player, RepairTargetOf(farm));
    if (start.Error is { } invalidTarget)
    {
      return CommandResult.Rejected(command, context.Tick, invalidTarget);
    }

    BuildingEntity target = farm ?? throw new InvalidOperationException($"Construction admitió reparar {repair.FarmId}, que no existe.");
    var order = new TaskOrder(UnitOrderKind.RepairFarm, target.Id, world.MovementTargetOf(target.Id));
    TaskOrderPlan plan = TaskOrderPlan.Evaluate(
        world, command.Player, order, repair.Routes, context.Navigation, _movement, _ => null); // Construction ya validó el objetivo.
    if (!plan.AnyAccepted)
    {
      return plan.ToResult(command, context.Tick); // Ningún trabajador llega: rechazo completo, sin cobro.
    }

    if (start.Kind == RepairStartKind.StartAndPay)
    {
      SpendEvaluation spend = _economy.EvaluateSpend(world.Players[command.Player].Resources, _economy.GetFarmRepairCost());
      if (spend.Error is { } insufficient)
      {
        return CommandResult.Rejected(command, context.Tick, insufficient);
      }

      StartRepair(context, command.Player, target, spend);
    }
    else
    {
      EnsureRepairInProgress(target);
    }

    plan.CommitTo(context);
    return plan.ToResult(command, context.Tick);
  }

  /// <summary>
  /// Lo que Construction necesita saber del edificio (03 §4). Agotada: según el estado productivo de Economy. En reparación: si ya
  /// tiene un <see cref="FarmRepairState"/>. Nulo si no es un edificio que exista.
  /// </summary>
  private static RepairTargetInfo? RepairTargetOf(BuildingEntity? building) => building is null
      ? null
      : new RepairTargetInfo(
          building.Id,
          building.Owner,
          building.Type,
          building.IsCompleted,
          IsDepleted: building.FarmEconomy?.Status == FarmProductionStatus.Depleted,
          IsRepairInProgress: building.Repair is not null);

  /// <summary>
  /// Cobro y comienzo, en el mismo commit (03 §4): el saldo que dejó Economy, la reparación desde 0 y el evento. Si Construction pidió
  /// empezar una reparación sobre algo que no es una granja agotada sin reparar, lo frena el constructor de <see cref="BuildingEntity"/>.
  /// </summary>
  private static void StartRepair(TickContext context, PlayerId player, BuildingEntity farm, SpendEvaluation spend)
  {
    if (farm.Repair is not null)
    {
      throw new InvalidOperationException($"Construction pidió cobrar otra vez la reparación de {farm.Id}, que ya está en curso.");
    }

    WorldState world = context.World;
    BuildingEntity repairing = farm.WithRepair(new FarmRepairState(farm.Id, farm.Owner, workDone: 0));
    context.World = world.With(
        players: world.Players.SetItem(player, new PlayerState(player, spend.BalanceAfter)),
        buildings: world.Buildings.SetItem(farm.Id, repairing));
    context.Emit(new FarmRepairStarted(farm.Id));
  }

  /// <summary>Un <c>Join</c> solo tiene sentido si ya hay una reparación en curso; si no, Construction respondió algo incoherente.</summary>
  private static void EnsureRepairInProgress(BuildingEntity farm)
  {
    if (farm.Repair is null)
    {
      throw new InvalidOperationException($"Construction pidió sumarse a la reparación de {farm.Id}, pero no hay ninguna en curso.");
    }
  }
}
