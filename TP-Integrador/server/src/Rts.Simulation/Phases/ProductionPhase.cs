using Rts.Combat;
using Rts.Economy;
using Rts.Economy.Contracts;
using Rts.Movement.Model;
using Rts.Production;
using Rts.Production.Contracts;
using Rts.Production.Model;
using Rts.Shared.Collections;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Simulation.Diagnostics;
using Rts.Simulation.Engine;
using Rts.Simulation.State;

namespace Rts.Simulation.Phases;

/// <summary>
/// Fase 8 (06 §4, contrato 05 §4-5). Solo corre si la partida sigue después de Victory Rules (05 §6).
/// <list type="number">
/// <item>Arma la entrada de Production: los productores completados, la comida de cada jugador (después del commit de la fase 6) y
/// la vista de celdas ocupadas para buscar dónde aparecer.</item>
/// <item>Llama al calculador de Production (función pura).</item>
/// <item>Confirma en orden: el <b>cobro</b> de cada inicio (verificado con Economy, todo o nada), las colas y tareas de cada
/// productor, y la <b>aparición</b> de cada unidad nueva, con su ID, el HP máximo de Combat y orden Idle.</item>
/// </list>
/// Eventos (06 §5): primero <c>UnitSpawned</c> en el orden de aparición, después los <c>ProductionChanged</c> de Production.
/// </summary>
internal sealed class ProductionPhase : ITickPhase
{
  private readonly IProductionCalculator _calculator;
  private readonly IEconomyRules _economy;
  private readonly ICombatRules _combat;

  public ProductionPhase(IProductionCalculator calculator, IEconomyRules economy, ICombatRules combat)
  {
    _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    _combat = combat ?? throw new ArgumentNullException(nameof(combat));
  }

  public SimulationPhase Phase => SimulationPhase.Production;

  public void Execute(TickContext context)
  {
    WorldState world = context.World;
    if (world.IsGameplayOver)
    {
      return; // La partida terminó en la fase 7: no se inicia ni aparece nada (05 §6).
    }

    var input = new ProductionTickInput(
        context.Tick,
        [.. world.Buildings.Values.Select(building => building.Production).OfType<ProducerState>()],
        [.. world.Players.Values.Select(player => new PlayerFood(player.Player, player.Resources.Food))],
        SpawnViewOf(world));

    ProductionTickResult result = _calculator.CalculateTick(input);
    EnsureCoherent(world, context.Tick, result);
    Apply(context, result);
  }

  private void Apply(TickContext context, ProductionTickResult result)
  {
    WorldState world = context.World;
    var players = world.Players;
    foreach (ProductionStart start in result.Starts)
    {
      players = players.SetItem(start.Owner, Charge(players[start.Owner], start));
    }

    var buildings = world.Buildings;
    foreach (ProducerState update in result.ProducerUpdates)
    {
      buildings = buildings.SetItem(update.ProducerId, buildings[update.ProducerId].WithProduction(update));
    }

    var units = world.Units;
    EntityId lastEntityId = world.LastEntityId;
    var spawned = new List<UnitSpawned>();
    foreach (SpawnProposal spawn in result.Spawns)
    {
      lastEntityId = lastEntityId.Next();
      int hp = _combat.GetUnitMaxHp(spawn.UnitType);
      var unit = new UnitEntity(
          lastEntityId, spawn.Owner, spawn.UnitType, spawn.Position, hp, MovementOrder.Idle, EquatableArray<PositionXZ>.Empty,
          world.MapRevision, repathPendingSinceTick: null, lastPursuitRepathTick: null, gatherTicksAccumulated: 0,
          readyAtTick: context.Tick); // Un soldado nuevo está listo para atacar desde ya (02 §5).
      units = units.SetItem(unit.Id, unit);
      spawned.Add(new UnitSpawned(unit.Id, unit.Owner, unit.Type, unit.Position, unit.Hp));
    }

    context.World = world.With(players: players, buildings: buildings, units: units, lastEntityId: lastEntityId);
    foreach (UnitSpawned unitSpawned in spawned)
    {
      context.Emit(unitSpawned);
    }

    foreach (IDomainEventPayload proposed in result.ProposedEvents)
    {
      context.Emit(proposed);
    }
  }

  /// <summary>
  /// Cobra un inicio con la regla de gasto de Economy (05 §5). Production ya verificó que alcanzaba con la comida de la entrada;
  /// si acá no alcanza, la propuesta es incoherente: falla crítica, nunca un cobro a medias.
  /// </summary>
  private PlayerState Charge(PlayerState player, ProductionStart start)
  {
    SpendEvaluation spend = _economy.EvaluateSpend(player.Resources, ResourceAmount.OfFood(start.PaidFood));
    if (!spend.IsAffordable)
    {
      throw new InvalidOperationException($"Production inició {start.UnitType} para {start.Owner} sin comida suficiente.");
    }

    return new PlayerState(player.Player, spend.BalanceAfter);
  }

  /// <summary>
  /// Celdas que no sirven para aparecer (05 §4): huellas de todos los edificios (también obras), celdas reservadas por árboles
  /// (aunque estén agotados, D-040) y la celda donde está el punto de cada unidad (D-004).
  /// </summary>
  private static SpawnView SpawnViewOf(WorldState world) => new(
      world.Grid,
      world.Buildings.Values.SelectMany(building => building.Footprint.Cells()),
      world.Trees.Values.Select(tree => tree.Cell),
      world.Units.Values.Select(unit => world.Grid.ToCell(unit.Position)));

  /// <summary>
  /// Coherencia: mismo tick; todo productor nombrado existe y produce; cada inicio cobra exactamente el costo de Economy; cada unidad
  /// aparece dentro del mapa.
  /// </summary>
  private void EnsureCoherent(WorldState world, TickId tick, ProductionTickResult result)
  {
    Require(result.Tick == tick, $"Production respondió el tick {result.Tick} en lugar de {tick}.");
    IEnumerable<EntityId> named = result.Starts.Select(start => start.ProducerId)
        .Concat(result.Spawns.Select(spawn => spawn.ProducerId))
        .Concat(result.ProducerUpdates.Select(update => update.ProducerId));
    foreach (EntityId producerId in named)
    {
      Require(
          world.Buildings.TryGetValue(producerId, out BuildingEntity? producer) && producer.Production is not null,
          $"Production nombró a {producerId}, que no es un productor completado.");
    }

    foreach (ProductionStart start in result.Starts)
    {
      ResourceAmount cost = _economy.GetTrainingCost(start.UnitType);
      Require(cost == ResourceAmount.OfFood(start.PaidFood), $"Production cobró {start.PaidFood} de comida por {start.UnitType}; el costo es {cost}.");
    }

    foreach (SpawnProposal spawn in result.Spawns)
    {
      Require(world.Grid.Contains(spawn.Cell), $"Production propuso aparecer fuera del mapa, en {spawn.Cell}.");
    }
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
