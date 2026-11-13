using System.Collections.Concurrent;
using Rts.Combat.Contracts;
using Rts.Economy.Contracts;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.World;
using Rts.Simulation.Commands;
using Rts.Simulation.Composition;
using Rts.Simulation.Configuration;
using Rts.Simulation.Map;
using Rts.Simulation.State;
using Rts.Simulation.Tests.Fakes;
using Rts.Simulation.Tests.Support;

namespace Rts.Simulation.Tests.Engine;

/// <summary>
/// Determinismo de punta a punta (06 §6, §11; D-051; AGENTS §7): el mismo mapa y el mismo guion de comandos, corridos con Economy ∥ Combat
/// en paralelo (terminando en distinto orden) y en secuencia, producen <b>exactamente</b> las mismas publicaciones y el mismo mundo final.
/// </summary>
public class DeterminismTests
{
  private const int TicksToRun = 25;
  private const int WoodPerGatherer = 1;
  private const int SoldierDamage = 8;
  private const int SoldierCooldownTicks = 2;
  private const string EconomyBranch = "Economy";
  private const string CombatBranch = "Combat";

  /// <summary>Lo que se demora la rama "lenta" de cada tick, para forzar el orden de llegada a la barrera.</summary>
  private static readonly TimeSpan SlowBranchDelay = TimeSpan.FromMilliseconds(15);

  // IDs que asigna InitialWorldFactory: primero los árboles, después P1 (Centro y unidades en orden) y después P2.
  private static readonly EntityId Tree = new(1);
  private static readonly EntityId P1Center = new(2);
  private static readonly EntityId P1Gatherer = new(3);
  private static readonly EntityId P1Builder = new(4);
  private static readonly EntityId P1Soldier = new(5);
  private static readonly EntityId P2Center = new(6);
  private static readonly EntityId P2Worker = new(7);
  private static readonly EntityId P2Soldier = new(8);

  private static readonly GridCell FarmAnchor = new(25, 10);
  private static readonly PositionXZ NextToFarm = new(24.5, 10.5);
  private static readonly PositionXZ P2WorkerStop = new(31.5, 33.5);
  private static readonly PositionXZ P2WorkerDestination = new(30.5, 30.5);
  private static readonly ClientCommandId BuildCommandId = new(2);

  private static readonly MapDefinition DuelMap = new(
      new GridSpec(1.0, 40, 40),
      [new GridCell(10, 20)],
      [
        new PlayerStartDefinition(PlayerId.One, new GridCell(2, 2),
        [
          new InitialUnitDefinition(UnitType.Worker, TestWorld.NextToTree),
          new InitialUnitDefinition(UnitType.Worker, new PositionXZ(7.5, 3.5)),
          new InitialUnitDefinition(UnitType.Soldier, new PositionXZ(20.5, 20.5)),
        ]),
        new PlayerStartDefinition(PlayerId.Two, new GridCell(34, 34),
        [
          new InitialUnitDefinition(UnitType.Worker, new PositionXZ(32.5, 36.5)),
          new InitialUnitDefinition(UnitType.Soldier, new PositionXZ(21.5, 20.5)),
        ]),
      ]);

  /// <summary>Qué rama se demora en los ticks pares (en los impares, la otra). En secuencia no hay demoras.</summary>
  private enum Pace
  {
    Sequential,
    EconomySlowOnEvenTicks,
    CombatSlowOnEvenTicks,
  }

  private sealed record MatchRun(IReadOnlyList<TickPublication> Publications, WorldView Final);

  [Fact]
  public void ParallelInEitherFinishingOrderAndSequential_ProduceIdenticalPublicationsAndWorld()
  {
    var finishOrder = new ConcurrentQueue<(long Tick, string Branch)>();

    MatchRun sequential = RunMatch(Pace.Sequential, finishOrder: null);
    MatchRun economySlow = RunMatch(Pace.EconomySlowOnEvenTicks, finishOrder);
    MatchRun combatSlow = RunMatch(Pace.CombatSlowOnEvenTicks, finishOrder);

    Assert.Equal(sequential.Publications, economySlow.Publications);
    Assert.Equal(sequential.Publications, combatSlow.Publications);
    Assert.Equal(sequential.Final, economySlow.Final);
    Assert.Equal(sequential.Final, combatSlow.Final);
    AssertBothFinishingOrdersHappened(finishOrder);
  }

  [Fact]
  public void TheScriptedMatch_ExercisesEveryPhaseThatCouldDiverge()
  {
    MatchRun run = RunMatch(Pace.EconomySlowOnEvenTicks, finishOrder: null);
    IDomainEventPayload[] events = [.. run.Publications.SelectMany(publication => publication.Events).Select(stamped => stamped.Payload)];

    Assert.Contains(events, payload => payload is BuildingPlaced);
    Assert.Contains(events, payload => payload is ProductionChanged);
    Assert.Contains(events, payload => payload is EntityDamaged);
    Assert.Contains(new EntityDestroyed(P1Soldier), events); // Ataques mutuos: caen los dos soldados en el mismo tick.
    Assert.Contains(new EntityDestroyed(P2Soldier), events);
    Assert.Contains(events, payload => payload is ResourcesChanged { Player: var player } && player == PlayerId.One);
    CommandResult[] buildResults = [.. run.Publications
        .SelectMany(publication => publication.CommandResults)
        .Where(result => result.Key == new IdempotencyKey(PlayerId.One, BuildCommandId))];
    Assert.Equal(2, buildResults.Length); // El original y la respuesta al reenvío...
    Assert.Equal(buildResults[0].Tick, buildResults[1].Tick); // ...que repite el resultado del tick original.
    Assert.Single(run.Final.Buildings, building => building.BuildingType == BuildingType.Farm); // El Build duplicado no creó otra obra.
  }

  /// <summary>Corre el guion completo con un motor nuevo y devuelve todas las publicaciones y el mundo final.</summary>
  private static MatchRun RunMatch(Pace pace, ConcurrentQueue<(long Tick, string Branch)>? finishOrder)
  {
    SimulationConfig config = ConfigAndMapTests.ValidConfig() with { RunDomainPhaseInParallel = pace != Pace.Sequential };
    var queue = new CommandQueue(config.CommandQueueCapacity);
    var economy = new FakeEconomyCalculator { Respond = input => Paced(pace, EconomyBranch, input.Tick, finishOrder, () => Gather(input)) };
    var combat = new FakeCombatCalculator { Respond = input => Paced(pace, CombatBranch, input.Tick, finishOrder, () => Fight(input)) };
    ISimulationEngine engine = SimulationFactory.CreateEngine(
        config, DuelMap, TestWorld.Economy, queue, TestModules.Create(economyCalculator: economy, combatCalculator: combat), TimeProvider.System);

    engine.InitializeMatch();
    Assert.Equal(UnitType.Soldier, engine.Current!.Units[P1Soldier].Type); // Los IDs del guion son los que se esperan.
    var publications = new List<TickPublication>();
    for (long tick = 1; tick <= TicksToRun; tick++)
    {
      foreach ((PlayerId player, ClientCommandId id, IGameCommand command) in Script(tick))
      {
        queue.Admit(player, id, command);
      }

      TickOutcome outcome = engine.AdvanceTick();
      Assert.Null(outcome.Failure);
      publications.Add(outcome.Publication!);
    }

    return new MatchRun(publications, WorldViewProjector.Project(engine.Current!));
  }

  /// <summary>Los comandos que los dos jugadores mandan antes de cada tick.</summary>
  private static IEnumerable<(PlayerId Player, ClientCommandId Id, IGameCommand Command)> Script(long tick)
  {
    BuildCommand build = BuildCommand.Place(BuildingType.Farm, FarmAnchor, MapRevision.Initial, [Route(P1Builder, NextToFarm)]);
    return tick switch
    {
      1 =>
      [
        (PlayerId.One, new ClientCommandId(1), new GatherWoodCommand(Tree, MapRevision.Initial, [Route(P1Gatherer, TestWorld.NextToTree)])),
        (PlayerId.Two, new ClientCommandId(1), new AttackCommand(P1Soldier, MapRevision.Initial, [Route(P2Soldier)])),
        (PlayerId.One, new ClientCommandId(3), new AttackCommand(P2Soldier, MapRevision.Initial, [Route(P1Soldier)])),
      ],
      2 =>
      [
        (PlayerId.One, BuildCommandId, build),
        (PlayerId.Two, new ClientCommandId(2), new TrainUnitCommand(P2Center, UnitType.Worker)),
      ],
      3 =>
      [
        (PlayerId.Two, new ClientCommandId(3), new MoveCommand(
            P2WorkerDestination, MapRevision.Initial, [Route(P2Worker, P2WorkerStop, P2WorkerDestination)])),
        (PlayerId.One, BuildCommandId, build), // Reenvío: la idempotencia responde el resultado original, sin cobrar de nuevo.
      ],
      6 => [(PlayerId.One, new ClientCommandId(4), new StopCommand([P1Builder]))],
      _ => [],
    };
  }

  private static UnitRouteCandidate Route(EntityId unit, params PositionXZ[] waypoints) => new(unit, [.. waypoints]);

  /// <summary>Regla simple de Economy para la prueba: cada recolector habilitado aporta madera a su dueño.</summary>
  private static EconomyTickResult Gather(EconomyTickInput input) => EconomyTickResult.Empty(input.Tick) with
  {
    PlayerDeltas = [.. input.Gatherers
        .GroupBy(gatherer => gatherer.Owner)
        .OrderBy(group => group.Key)
        .Select(group => new PlayerResourceDelta(group.Key, ResourceAmount.OfWood(group.Count() * WoodPerGatherer)))],
  };

  /// <summary>Regla simple de Combat para la prueba: cada soldado listo hace daño a su objetivo y vuelve a estar listo en dos ticks.</summary>
  private static CombatTickResult Fight(CombatTickInput input)
  {
    Dictionary<EntityId, int> hp = input.Units.ToDictionary(unit => unit.Id, unit => unit.Hp);
    foreach (var building in input.Buildings)
    {
      hp[building.Id] = building.Hp;
    }

    var ready = input.Soldiers.Where(soldier => soldier.ReadyAtTick <= input.Tick && hp.ContainsKey(soldier.TargetId)).ToList();
    TargetDamage[] damage = [.. ready
        .GroupBy(soldier => soldier.TargetId)
        .OrderBy(group => group.Key)
        .Select(group => new TargetDamage(group.Key, hp[group.Key], group.Count() * SoldierDamage))];
    IDomainEventPayload[] events = [.. damage.Select(hit => hit.HpBefore > hit.TotalDamage
        ? (IDomainEventPayload)new EntityDamaged(hit.TargetId, hit.HpBefore - hit.TotalDamage)
        : new EntityDestroyed(hit.TargetId))];

    return new CombatTickResult(
        input.Tick,
        [.. ready.Select(soldier => new AttackPerformed(soldier.SoldierId, soldier.TargetId, SoldierDamage))],
        [.. damage],
        [.. ready.Select(soldier => new CooldownUpdate(soldier.SoldierId, input.Tick.Plus(SoldierCooldownTicks)))],
        default,
        [.. events]);
  }

  /// <summary>Demora la rama que toca según el tick y anota en qué orden terminaron. En secuencia, no demora nada.</summary>
  private static T Paced<T>(Pace pace, string branch, TickId tick, ConcurrentQueue<(long Tick, string Branch)>? finishOrder, Func<T> calculate)
  {
    bool evenTick = tick.Value % 2 == 0;
    string slowBranch = (pace == Pace.EconomySlowOnEvenTicks) == evenTick ? EconomyBranch : CombatBranch;
    if (pace != Pace.Sequential && branch == slowBranch)
    {
      Thread.Sleep(SlowBranchDelay);
    }

    T result = calculate();
    finishOrder?.Enqueue((tick.Value, branch));
    return result;
  }

  /// <summary>La prueba solo vale si, de verdad, a veces Economy terminó primero y a veces Combat.</summary>
  private static void AssertBothFinishingOrdersHappened(ConcurrentQueue<(long Tick, string Branch)> finishOrder)
  {
    var firstToFinish = finishOrder
        .GroupBy(entry => entry.Tick)
        .Select(group => group.First().Branch)
        .ToHashSet();
    Assert.Contains(EconomyBranch, firstToFinish);
    Assert.Contains(CombatBranch, firstToFinish);
  }
}
