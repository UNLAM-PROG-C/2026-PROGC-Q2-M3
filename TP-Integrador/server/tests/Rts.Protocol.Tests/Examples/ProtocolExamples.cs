using System.Text.Json;
using Rts.Protocol.Commands;
using Rts.Protocol.Session;
using Rts.Protocol.State;
using Rts.Protocol.Transport;
using Rts.Shared.Commands;
using Rts.Shared.Entities;
using Rts.Shared.Errors;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.Protocol.Tests.Examples;

/// <summary>
/// Catálogo de los ejemplos de <c>docs/protocol/examples/</c> (para el cliente Godot). Se arman con los DTOs reales y se serializan
/// con <see cref="ProtocolJson.Options"/>, así que son exactamente lo que envía y espera el servidor. Todos pertenecen a un mismo
/// mundo, visto por P1, para que puedan aplicarse en orden sobre el InitialState.
/// <para>Los números de balance son los valores TEMPORALES de <c>server/config/server.example.json</c>.</para>
/// </summary>
internal static class ProtocolExamples
{
    public const string SessionId = "5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41";

    public sealed record Example(string RelativePath, string Type, long? MessageId, object? Payload);

    /// <summary>Opciones del protocolo, pero indentadas para que los archivos se lean fácil. En el cable cada mensaje va en una línea.</summary>
    private static readonly JsonSerializerOptions Indented = new(ProtocolJson.Options) { WriteIndented = true };

    public static IReadOnlyList<Example> All { get; } = [.. ServerToClient(), .. ClientToServer()];

    /// <summary>Mensaje completo, con su sobre, tal como lo vería Godot (pero indentado).</summary>
    public static string Render(Example example)
    {
        JsonElement? payload = example.Payload is null
            ? null
            : JsonSerializer.SerializeToElement(example.Payload, example.Payload.GetType(), ProtocolJson.Options);
        var envelope = new NetworkEnvelope(NetworkEnvelope.InitialVersion, example.Type, example.MessageId, payload);
        return JsonSerializer.Serialize(envelope, Indented) + "\n";
    }

    private static IEnumerable<Example> ServerToClient()
    {
        const string dir = "server-to-client/";

        yield return new(dir + "01-player-assigned.json", ProtocolMessageTypes.PlayerAssigned, null, new PlayerAssignedDto(SessionId, 1));
        yield return new(dir + "02-initial-state.json", ProtocolMessageTypes.InitialState, null, InitialState());
        yield return new(dir + "03-start-aborted.json", ProtocolMessageTypes.StartAborted, null, new StartAbortedDto(1, StartAbortReason.OpponentLeft));
        yield return new(dir + "04-match-started.json", ProtocolMessageTypes.MatchStarted, null, new MatchStartedDto(SessionId, 1));

        // Tick 1: se procesan el Move (messageId 1) y el GatherWood (messageId 2) de P1.
        yield return Events(dir + "05-state-events-orders-accepted.json", tick: 1, mapRevision: 0,
            [new UnitOrderChangedDto(0, 3, 1, UnitOrderKind.Move), new UnitOrderChangedDto(1, 4, 2, UnitOrderKind.GatherWood)],
            [Accepted(1, 1, 1, 3), Accepted(2, 2, 1, 4)]);

        // Tick 2: primera actualización de posiciones (cada 2 ticks). El trabajador 3 avanza 0,3 m hacia (10.5, 6.5).
        yield return new(dir + "06-position-update.json", ProtocolMessageTypes.PositionUpdate, null, new PositionUpdateDto(2,
        [
            new UnitPositionDto(3, 7.712, 3.712),
            new UnitPositionDto(4, 7.5, 5.5),
            new UnitPositionDto(5, 32.5, 36.5),
            new UnitPositionDto(6, 32.5, 34.5),
        ]));

        // Tick 6: Build (messageId 3) coloca una granja; paga 60 de madera; cambia el mapa.
        yield return Events(dir + "07-state-events-building-placed.json", tick: 6, mapRevision: 1,
        [
            new BuildingPlacedDto(0, 11, 1, BuildingType.Farm, new FootprintDto(8, 10, 2, 2), 20),
            new MapRevisionChangedDto(1, 1),
            new UnitOrderChangedDto(2, 3, 3, UnitOrderKind.Build),
            new ResourcesChangedDto(3, 140, 100),
        ],
        [Accepted(3, 3, 6, 3)]);

        // Tick 7: la granja nueva cortó la ruta del trabajador 4; el servidor pide una ruta nueva (solo al dueño).
        yield return Events(dir + "08-state-events-repath-requested.json", tick: 7, mapRevision: 1,
            [new RepathRequestedDto(0, 4, 2, 1, null, 7, IsPaused: true)], []);

        // Tick 30: TrainUnit (messageId 4): el Centro Principal empieza a entrenar un trabajador y cobra 50 de comida.
        yield return Events(dir + "09-state-events-production-started.json", tick: 30, mapRevision: 1,
        [
            new ProductionChangedDto(2, 1, ProducerStatus.Training, UnitType.Worker, 200, []),
            new ResourcesChangedDto(3, 150, 50),
        ],
        [Accepted(4, 4, 30, 1)]);

        // Tick 40: un Move con una unidad propia y una rival (parcial) y un Build sin madera suficiente (rechazado).
        yield return Events(dir + "10-state-events-command-results.json", tick: 40, mapRevision: 1,
            [new UnitOrderChangedDto(0, 4, 5, UnitOrderKind.Move)],
            [
                new CommandResultDto(5, 5, 40, CommandOutcome.PartiallyAccepted, null,
                    [new EntityResultDto(4, null), new EntityResultDto(5, CommonErrors.NotOwner.Value)]),
                new CommandResultDto(8, 6, 40, CommandOutcome.Rejected, "Economy.InsufficientWood", []),
            ]);

        // Tick 186: la granja llega a su HP máximo, queda completada y el constructor pasa a Idle.
        yield return Events(dir + "11-state-events-building-completed.json", tick: 186, mapRevision: 1,
        [
            new BuildingProgressChangedDto(0, 11, 200),
            new BuildingCompletedDto(1, 11),
            new UnitOrderChangedDto(2, 3, 0, UnitOrderKind.Idle),
        ], []);

        // Tick 230: aparece el trabajador entrenado y el productor queda libre.
        yield return Events(dir + "12-state-events-unit-spawned.json", tick: 230, mapRevision: 1,
        [
            new UnitSpawnedDto(0, 12, 1, UnitType.Worker, 6.5, 2.5, 40),
            new ProductionChangedDto(1, 1, ProducerStatus.Idle, null, null, []),
        ], []);

        // Tick 410: el árbol 7 se agota con la última recolección; su recolector pasa a Idle.
        yield return Events(dir + "13-state-events-tree-depleted.json", tick: 410, mapRevision: 1,
        [
            new TreeDepletedDto(0, 7),
            new UnitOrderChangedDto(1, 4, 0, UnitOrderKind.Idle),
            new ResourcesChangedDto(2, 240, 50),
        ], []);

        // Tick 690: la granja entrega su última tanda y queda agotada.
        yield return Events(dir + "14-state-events-farm-depleted.json", tick: 690, mapRevision: 1,
            [new FarmDepletedDto(0, 11), new ResourcesChangedDto(1, 240, 85)], []);

        // Tick 700: RepairFarm (messageId 6): empieza la reparación y cobra 30 de madera.
        yield return Events(dir + "15-state-events-farm-repair-started.json", tick: 700, mapRevision: 1,
        [
            new FarmRepairStartedDto(0, 11),
            new UnitOrderChangedDto(1, 3, 6, UnitOrderKind.RepairFarm),
            new ResourcesChangedDto(2, 210, 85),
        ],
        [Accepted(6, 9, 700, 3)]);

        // Tick 800: la reparación llega al trabajo requerido; la granja vuelve a producir.
        yield return Events(dir + "16-state-events-farm-repair-completed.json", tick: 800, mapRevision: 1,
        [
            new FarmRepairProgressChangedDto(0, 11, 100, 100),
            new FarmRepairCompletedDto(1, 11),
            new UnitOrderChangedDto(2, 3, 0, UnitOrderKind.Idle),
        ], []);

        // Tick 1610: el árbol 7 vuelve a estar disponible (mismo ID, misma celda).
        yield return Events(dir + "17-state-events-tree-respawned.json", tick: 1610, mapRevision: 1,
            [new TreeRespawnedDto(0, 7)], []);

        // Tick 2010: un soldado rival daña al trabajador 3.
        yield return Events(dir + "18-state-events-entity-damaged.json", tick: 2010, mapRevision: 1,
            [new EntityDamagedDto(0, 3, 32)], []);

        // Tick 2100: mueren el trabajador 3 y la granja 11; el mapa cambia porque cayó un edificio.
        yield return Events(dir + "19-state-events-entity-destroyed.json", tick: 2100, mapRevision: 2,
            [new EntityDestroyedDto(0, 3), new EntityDestroyedDto(1, 11), new MapRevisionChangedDto(2, 2)], []);

        yield return new(dir + "20-command-rejected.json", ProtocolMessageTypes.CommandRejected, null,
            new CommandRejectedDto(9, CommonErrors.MatchNotRunning.Value));
        yield return new(dir + "21-protocol-error.json", ProtocolMessageTypes.ProtocolError, null,
            new ProtocolErrorDto("Network.MalformedJson", null, "Se esperaba un objeto JSON.", null));
        yield return new(dir + "22-protocol-error-unsupported-version.json", ProtocolMessageTypes.ProtocolError, null,
            new ProtocolErrorDto("Network.UnsupportedVersion", null, "El servidor solo acepta la versión 1.", 1));
        yield return new(dir + "23-session-request-rejected.json", ProtocolMessageTypes.SessionRequestRejected, null,
            new SessionRequestRejectedDto("Session.StaleInitialization", 1));
        yield return new(dir + "24-ping.json", ProtocolMessageTypes.Ping, null, new PingDto(1));
        yield return new(dir + "25-pong.json", ProtocolMessageTypes.Pong, null, new PongDto(7));
        yield return new(dir + "26-match-ended-victory.json", ProtocolMessageTypes.MatchEnded, null,
            new MatchEndedDto(SessionId, MatchResultType.Victory, 1, MatchEndReason.BaseDestroyed, 5400));
        yield return new(dir + "27-match-ended-draw.json", ProtocolMessageTypes.MatchEnded, null,
            new MatchEndedDto(SessionId, MatchResultType.Draw, null, MatchEndReason.BothBasesDestroyed, 5400));
        yield return new(dir + "28-match-ended-disconnect.json", ProtocolMessageTypes.MatchEnded, null,
            new MatchEndedDto(SessionId, MatchResultType.Victory, 1, MatchEndReason.PlayerDisconnected, 3100));
        yield return new(dir + "29-disconnecting.json", ProtocolMessageTypes.Disconnecting, null,
            new DisconnectingDto("Network.ServerShuttingDown"));
    }

    private static IEnumerable<Example> ClientToServer()
    {
        const string dir = "client-to-server/";

        yield return new(dir + "hello.json", ProtocolMessageTypes.Hello, null, new HelloDto("godot-agus"));
        yield return new(dir + "client-ready.json", ProtocolMessageTypes.ClientReady, null, new ClientReadyDto(1));
        yield return new(dir + "ping.json", ProtocolMessageTypes.Ping, null, new PingDto(7));
        yield return new(dir + "pong.json", ProtocolMessageTypes.Pong, null, new PongDto(1));
        yield return new(dir + "move.json", ProtocolMessageTypes.Move, 1,
            new MoveCommandDto(new PositionDto(10.5, 6.5), 0, [Route(3, (9.0, 5.0), (10.5, 6.5))]));
        yield return new(dir + "gather-wood.json", ProtocolMessageTypes.GatherWood, 2,
            new GatherWoodCommandDto(7, 0, [Route(4, (9.5, 6.5), (11.5, 7.5))]));
        yield return new(dir + "build-place.json", ProtocolMessageTypes.Build, 3,
            new BuildCommandDto(BuildingType.Farm, 8, 10, null, 0, [Route(3, (9.0, 9.5))]));
        yield return new(dir + "train-unit.json", ProtocolMessageTypes.TrainUnit, 4, new TrainUnitCommandDto(1, UnitType.Worker));
        yield return new(dir + "repair-farm.json", ProtocolMessageTypes.RepairFarm, 6,
            new RepairFarmCommandDto(11, 1, [Route(3, (9.0, 9.5))]));
        yield return new(dir + "build-continue.json", ProtocolMessageTypes.Build, 10,
            new BuildCommandDto(null, null, null, 11, 1, [Route(4, (10.5, 9.5))]));
        yield return new(dir + "attack.json", ProtocolMessageTypes.Attack, 11,
            new AttackCommandDto(13, 1, [Route(14, (20.5, 20.5), (30.5, 30.0))]));
        yield return new(dir + "stop.json", ProtocolMessageTypes.Stop, 12, new StopCommandDto([3, 4]));
        yield return new(dir + "repath-response.json", ProtocolMessageTypes.RepathResponse, 13,
            new RepathResponseCommandDto(4, 2, 1, [new PositionDto(7.5, 6.5), new PositionDto(11.5, 6.5), new PositionDto(11.5, 7.5)]));
    }

    private static InitialStateDto InitialState() => new(
        SessionId: SessionId,
        InitializationId: 1,
        PlayerId: 1,
        Tick: 0,
        Map: new MapDto(1.0, 40, 40, 0),
        Players: [new PlayerPublicDto(1, 1), new PlayerPublicDto(2, 2)],
        Units:
        [
            new UnitDto(3, 1, UnitType.Worker, 7.5, 3.5, 40, UnitOrderKind.Idle),
            new UnitDto(4, 1, UnitType.Worker, 7.5, 5.5, 40, UnitOrderKind.Idle),
            new UnitDto(5, 2, UnitType.Worker, 32.5, 36.5, 40, UnitOrderKind.Idle),
            new UnitDto(6, 2, UnitType.Worker, 32.5, 34.5, 40, UnitOrderKind.Idle),
        ],
        Buildings:
        [
            new BuildingDto(1, 1, BuildingType.MainCenter, new FootprintDto(2, 2, 4, 4), 1000, true, null),
            new BuildingDto(2, 2, BuildingType.MainCenter, new FootprintDto(34, 34, 4, 4), 1000, true, null),
        ],
        Trees:
        [
            new TreeDto(7, 12, 8, 12.5, 8.5, true),
            new TreeDto(8, 13, 8, 13.5, 8.5, true),
            new TreeDto(9, 26, 30, 26.5, 30.5, true),
            new TreeDto(10, 27, 30, 27.5, 30.5, true),
        ],
        OwnResources: new ResourcesDto(200, 100),
        OwnProduction: [new ProducerDto(1, ProducerStatus.Idle, null, null, [])],
        Catalog: new CatalogDto(
            TicksPerSecond: 20,
            Units:
            [
                new UnitCatalogDto(UnitType.Worker, 40, new ResourcesDto(0, 50), 200, 3.0, null),
                new UnitCatalogDto(UnitType.Soldier, 80, new ResourcesDto(0, 80), 300, 3.5, new AttackDto(8, 1.5, 20)),
            ],
            Buildings:
            [
                new BuildingCatalogDto(BuildingType.MainCenter, 1000, 4, 4, null, null),
                new BuildingCatalogDto(BuildingType.Farm, 200, 2, 2, new ResourcesDto(60, 0), null),
                new BuildingCatalogDto(BuildingType.Barracks, 400, 3, 3, new ResourcesDto(150, 0), null),
                new BuildingCatalogDto(BuildingType.Tower, 300, 2, 2, new ResourcesDto(120, 0), new AttackDto(10, 6.0, 30)),
                new BuildingCatalogDto(BuildingType.Fence, 100, 1, 1, new ResourcesDto(5, 0), null),
            ],
            FarmRepairCost: new ResourcesDto(30, 0),
            FarmRepairWorkRequired: 100,
            GatherRange: 1.5,
            BuildRange: 1.5));

    private static Example Events(string path, long tick, long mapRevision, IReadOnlyList<StateEventDto> events, IReadOnlyList<CommandResultDto> results) =>
        new(path, ProtocolMessageTypes.StateEvents, null, new StateEventsDto(tick, mapRevision, events, results));

    private static CommandResultDto Accepted(long clientCommandId, long sequence, long tick, int entityId) =>
        new(clientCommandId, sequence, tick, CommandOutcome.Accepted, null, [new EntityResultDto(entityId, null)]);

    private static UnitRouteDto Route(int unitId, params (double X, double Z)[] waypoints) =>
        new(unitId, waypoints.Select(w => new PositionDto(w.X, w.Z)).ToArray());
}
