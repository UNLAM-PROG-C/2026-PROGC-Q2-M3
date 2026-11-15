using Rts.Integration.Tests.Support;
using Rts.Server.Configuration;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Simulation.Map;
using Rts.Simulation.State;

namespace Rts.Integration.Tests;

/// <summary>
/// Prueba de humo de la integración: el motor de Simulation con Economy, Combat y Movement <b>reales</b>, la configuración de
/// <c>server.example.json</c> y comandos como los mandaría un cliente. No busca cubrir reglas (eso lo hacen las pruebas de cada
/// módulo), sino que las piezas encajen: que un comando aceptado termine en el efecto esperado y que la partida llegue a su fin.
/// Cada escenario corre con Economy ∥ Combat en paralelo y en secuencia, y las dos corridas tienen que ser idénticas (D-051).
/// </summary>
public class SmokeTests
{
  /// <summary>Holgura para caminar al bosque: el trabajador recorre menos de 4 celdas, unos 25 ticks con la config de ejemplo.</summary>
  private const int WalkTicksAllowance = 100;

  /// <summary>Margen sobre el intervalo de recolección: el comando se procesa recién en el tick siguiente al envío.</summary>
  private const int GatherTicksSafetyFactor = 2;

  /// <summary>Margen sobre el cálculo ideal de ticks para destruir la base (el primer golpe puede esperar un cooldown).</summary>
  private const int SiegeTicksSafetyFactor = 2;

  private const double SiegeMapCellSize = 1.0;
  private const int SiegeMapCells = 16;

  // Mapa real (default.json): el primer trabajador de P1 y el árbol más cercano del bosque de P1.
  private static readonly PositionXZ GathererStart = new(9.5, 5.5);
  private static readonly GridCell NearestTreeCell = new(14, 5);
  private static readonly PositionXZ NextToNearestTree = new(13.2, 5.5); // Línea recta por la fila 5, sin cruzar nada.

  // Mapa de asedio: dos soldados de P1 ya pegados al Centro Principal de P2 (columnas 11 a 14), sin necesidad de caminar.
  private static readonly GridCell P1CenterAnchor = new(1, 1);
  private static readonly GridCell P2CenterAnchor = new(11, 11);
  private static readonly PositionXZ SoldierA = new(10.5, 12.5);
  private static readonly PositionXZ SoldierB = new(10.5, 13.5);
  private static readonly PositionXZ P2WorkerStart = new(8.5, 14.5);
  private static readonly GridCell LoneTree = new(8, 2);
  private static readonly PositionXZ[] Soldiers = [SoldierA, SoldierB];

  [Fact]
  public void WorkerMovedNextToATree_GathersAndIsCreditedWood_SameInParallelAndSequential()
  {
    ServerConfig config = IntegrationMatch.LoadConfig();

    (IntegrationMatch parallel, GatherCommands commands) = RunMoveThenGatherScenario(config, runDomainPhaseInParallel: true);
    (IntegrationMatch sequential, _) = RunMoveThenGatherScenario(config, runDomainPhaseInParallel: false);

    Assert.Equal(CommandOutcome.Accepted, parallel.ResultOf(commands.Move).Outcome);
    Assert.Equal(CommandOutcome.Accepted, parallel.ResultOf(commands.Gather).Outcome);
    AssertWoodGatheredOnce(config, parallel);
    AssertIdenticalRuns(parallel, sequential);
  }

  [Fact(Skip = "Pendiente de Movement (Ivan): avanzar hacia el objetivo en órdenes de tarea, contrato 04 §5 pasos 5 a 7. "
      + "Hoy MovementCalculator solo mueve órdenes Move, lo que pide el work-plan para la vertical slice.")]
  public void WorkerSentToGatherFromAfar_WalksToTheTreeAndIsCreditedWood()
  {
    ServerConfig config = IntegrationMatch.LoadConfig();
    var match = IntegrationMatch.Start(config, IntegrationMatch.LoadConfiguredMap(config), runDomainPhaseInParallel: true);
    UnitEntity worker = match.UnitAt(PlayerId.One, GathererStart);
    int initialWood = match.World.Players[PlayerId.One].Resources.Wood;

    match.Submit(PlayerId.One, GatherAlong(match, worker, [NextToNearestTree]));
    match.AdvanceUntil(
        world => world.Players[PlayerId.One].Resources.Wood > initialWood,
        WalkTicksAllowance + config.Economy.GatherIntervalTicks);

    AssertWoodGatheredOnce(config, match);
  }

  private static void AssertWoodGatheredOnce(ServerConfig config, IntegrationMatch match)
  {
    int woodPerGather = config.Economy.WoodPerGather;
    Assert.Equal(config.Economy.InitialBalance.Wood + woodPerGather, match.World.Players[PlayerId.One].Resources.Wood);
    Assert.Equal(config.Economy.InitialBalance, match.World.Players[PlayerId.Two].Resources); // P2 no hizo nada.
    Assert.Equal(config.Economy.MaxWoodPerTree - woodPerGather, match.TreeAt(NearestTreeCell).Economy.WoodRemaining);
  }

  [Fact]
  public void SoldiersAttackingTheEnemyMainCenter_DestroyItAndWinTheMatch_SameInParallelAndSequential()
  {
    ServerConfig config = IntegrationMatch.LoadConfig();

    (IntegrationMatch parallel, IdempotencyKey command) = RunSiegeScenario(config, runDomainPhaseInParallel: true);
    (IntegrationMatch sequential, _) = RunSiegeScenario(config, runDomainPhaseInParallel: false);

    Assert.Equal(CommandOutcome.Accepted, parallel.ResultOf(command).Outcome);
    MatchResult expected = MatchResult.BaseDestroyed(PlayerId.One, parallel.World.Tick);
    Assert.Equal(expected, parallel.World.GameplayResult);
    Assert.Equal(expected, parallel.Publications[^1].GameplayResult); // El último tick publica el fin de la partida.
    Assert.DoesNotContain(parallel.World.Buildings.Values, building => building.Owner == PlayerId.Two);
    AssertIdenticalRuns(parallel, sequential);
  }

  /// <summary>
  /// Mapa real: un trabajador de P1 camina con <c>Move</c> hasta el árbol (Movement real) y, ya quieto y en rango, recibe
  /// <c>GatherWood</c> sin ruta (Economy real). Se avanza hasta la primera madera.
  /// </summary>
  private static (IntegrationMatch Match, GatherCommands Commands) RunMoveThenGatherScenario(
      ServerConfig config, bool runDomainPhaseInParallel)
  {
    var match = IntegrationMatch.Start(config, IntegrationMatch.LoadConfiguredMap(config), runDomainPhaseInParallel);
    UnitEntity worker = match.UnitAt(PlayerId.One, GathererStart);
    int initialWood = match.World.Players[PlayerId.One].Resources.Wood;

    IdempotencyKey move = match.Submit(PlayerId.One, new MoveCommand(
        NextToNearestTree, match.World.MapRevision, [new UnitRouteCandidate(worker.Id, [NextToNearestTree])]));
    match.AdvanceUntil(world => world.Units[worker.Id].Order.IsIdle, WalkTicksAllowance);
    Assert.Equal(NextToNearestTree, match.World.Units[worker.Id].Position);

    IdempotencyKey gather = match.Submit(PlayerId.One, GatherAlong(match, worker, []));
    match.AdvanceUntil(
        world => world.Players[PlayerId.One].Resources.Wood > initialWood,
        config.Economy.GatherIntervalTicks * GatherTicksSafetyFactor);
    return (match, new GatherCommands(move, gather));
  }

  private static GatherWoodCommand GatherAlong(IntegrationMatch match, UnitEntity worker, PositionXZ[] waypoints) =>
      new(match.TreeAt(NearestTreeCell).Id, match.World.MapRevision, [new UnitRouteCandidate(worker.Id, [.. waypoints])]);

  /// <summary>Mapa de asedio: los dos soldados de P1 atacan el Centro Principal de P2 sin moverse, hasta que la partida termina.</summary>
  private static (IntegrationMatch Match, IdempotencyKey Command) RunSiegeScenario(ServerConfig config, bool runDomainPhaseInParallel)
  {
    MapDefinition map = SiegeMap();
    Assert.Empty(map.GetValidationErrors(IntegrationMatch.MainCenterSize(config)));
    var match = IntegrationMatch.Start(config, map, runDomainPhaseInParallel);
    EntityId target = match.MainCenterOf(PlayerId.Two).Id;
    UnitRouteCandidate[] stayInPlace = [.. Soldiers.Select(position => new UnitRouteCandidate(match.UnitAt(PlayerId.One, position).Id, []))];

    IdempotencyKey command = match.Submit(PlayerId.One, new AttackCommand(target, match.World.MapRevision, [.. stayInPlace]));
    match.AdvanceUntil(world => world.IsGameplayOver, MaxTicksToDestroyMainCenter(config));
    return (match, command);
  }

  /// <summary>Golpes necesarios con los dos soldados, por el cooldown entre golpes, con margen. Sale de la config, no de números fijos.</summary>
  private static int MaxTicksToDestroyMainCenter(ServerConfig config)
  {
    int mainCenterHp = config.Construction.BuildingMaxHp[BuildingType.MainCenter];
    int damagePerVolley = config.Combat.Soldier.Damage * Soldiers.Length;
    int volleys = (mainCenterHp + damagePerVolley - 1) / damagePerVolley;
    return volleys * config.Combat.Soldier.CooldownTicks * SiegeTicksSafetyFactor;
  }

  private static MapDefinition SiegeMap() => new(
      new GridSpec(SiegeMapCellSize, SiegeMapCells, SiegeMapCells),
      [LoneTree],
      [
        new PlayerStartDefinition(PlayerId.One, P1CenterAnchor,
        [
          new InitialUnitDefinition(UnitType.Soldier, SoldierA),
          new InitialUnitDefinition(UnitType.Soldier, SoldierB),
        ]),
        new PlayerStartDefinition(PlayerId.Two, P2CenterAnchor, [new InitialUnitDefinition(UnitType.Worker, P2WorkerStart)]),
      ]);

  private sealed record GatherCommands(IdempotencyKey Move, IdempotencyKey Gather);

  /// <summary>Mismas publicaciones, tick por tick, y mismo mundo final: el paralelismo no cambia el resultado (D-051).</summary>
  private static void AssertIdenticalRuns(IntegrationMatch parallel, IntegrationMatch sequential)
  {
    Assert.Equal(sequential.Publications, parallel.Publications);
    Assert.Equal(WorldViewProjector.Project(sequential.World), WorldViewProjector.Project(parallel.World));
  }
}
