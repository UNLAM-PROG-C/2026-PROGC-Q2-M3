# Contrato 00 — Núcleo compartido (`Rts.Shared`)

> **Estado:** primera versión implementada (`server/src/Rts.Shared`) y probada (`server/tests/Rts.Shared.Tests`).
> **Etiquetas:** **DECIDIDO** = aprobado por el equipo (ver [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Alcance:** tipos base que comparten todos los módulos. **No** contiene reglas de gameplay, estado mutable, I/O ni DTOs JSON: los modelos de dominio, las interfaces entre módulos y los DTOs de red siguen siendo cosas distintas (AGENTS §4).

## 1. Estructura de la solución

**DECIDIDO (D-001, D-002) / RECOMENDADO (D-008):**

```
server/
  Rts.slnx
  global.json                 SDK .NET 10 (rollForward latestFeature)
  Directory.Build.props       net10.0, nullable, warnings como errores, build determinista
  Directory.Packages.props    versiones NuGet centralizadas
  src/
    Rts.Shared/               ← este contrato (sin dependencias)
    Rts.<Modulo>/             uno por módulo, se agregan al trabajar cada módulo
    Rts.Protocol/             esquema JSON (DTOs), contrato 09
    Rts.Server/               composición y programa principal (contrato 10)
  config/
    server.example.json       configuración de ejemplo con valores temporales
  tests/
    Directory.Build.props     xUnit + configuración de proyectos de prueba
    Rts.Shared.Tests/
    Rts.<Modulo>.Tests/
```

Reglas de dependencia previstas, que el compilador hace cumplir mediante referencias entre proyectos:

| Proyecto | Puede referenciar |
|---|---|
| `Rts.Shared` | nada |
| `Rts.Economy`, `Rts.Combat`, `Rts.Construction`, `Rts.Movement`, `Rts.Production` | `Rts.Shared` |
| `Rts.Simulation` | `Rts.Shared` + módulos de dominio (o sus abstracciones) |
| `Rts.Protocol` | `Rts.Shared` (solo enums de dominio y `PlayerId`; DTOs JSON, D-066) |
| `Rts.GameSession` | `Rts.Shared` |
| `Rts.StateSync` | `Rts.Shared` + `Rts.Protocol` |
| `Rts.Networking` | `Rts.Shared` + `Rts.Protocol` |
| `Rts.Server` | todos (solo composición, D-081) |

## 2. Espacios de nombres

| Namespace | Contenido |
|---|---|
| `Rts.Shared.Ids` | Identificadores tipados |
| `Rts.Shared.Geometry` | `PositionXZ`, `GridCell`, `GridSpec`, `FootprintSize`, `Footprint`, `WorldRect` |
| `Rts.Shared.Resources` | `ResourceAmount`, `ResourceType` |
| `Rts.Shared.Entities` | `UnitType`, `BuildingType`, `EntityKind`, `UnitOrderKind`, `ProducerStatus` |
| `Rts.Shared.Commands` | `CommandType`, `IGameCommand`, `CommandEnvelope`, `CommandOutcome`, `EntityCommandResult`, `CommandResult`, payloads de comandos, cola global (`ICommandSink`, `ICommandSource`, `CommandAdmission`) |
| `Rts.Shared.Events` | `IDomainEventPayload`, `EventStamp`, `DomainEvent`; payloads por módulo (Economy: `TreeDepleted`, `TreeRespawned`, `FarmDepleted`; Combat: `EntityDamaged`, `EntityDestroyed`; Movement: `UnitOrderChanged`, `RepathRequested`; Construction: `BuildingPlaced`, `BuildingProgressChanged`, `BuildingCompleted`, `FarmRepairStarted`, `FarmRepairProgressChanged`, `FarmRepairCompleted`, `MapRevisionChanged`; Production: `UnitSpawned`, `ProductionChanged`; Simulation: `ResourcesChanged`) |
| `Rts.Shared.Errors` | `ErrorCode`, `CommonErrors` |
| `Rts.Shared.Time` | `TickRate` |
| `Rts.Shared.Match` | `MatchResult`, `MatchResultType`, `MatchEndReason`, `ISimulationControl`, `IMatchResultSink` (ver [contrato 06](06-simulation.md)) |
| `Rts.Shared.World` | `WorldView` y sus vistas, `UnitPosition`, `TickPublication`, `ITickPublisher` (ver [contrato 06](06-simulation.md)); `GameCatalog` (ver [contrato 09](09-state-sync.md)) |
| `Rts.Shared.Session` | `SessionStatus`, `ISessionEventSink`, `ISessionGate`, mensajes de sesión (`PlayerAssigned`, `StartAborted`, `MatchStarted`, `MatchEnded`, `SessionRequestRejected`) (ver [contrato 07](07-game-session.md)) |
| `Rts.Shared.Collections` | `EquatableArray<T>` |

## 3. Identificadores

**DECIDIDO (D-003):** todos son `readonly record struct`, con igualdad por valor, y no se pueden mezclar entre sí.

| Tipo | Base | Asigna | Notas |
|---|---|---|---|
| `EntityId` | `int` | Simulation | Espacio **global** para unidades, edificios y árboles. `0` = `None`. Ordenable (desempate por menor ID). |
| `PlayerId` | `int` | GameSession | Solo `1` y `2` son válidos; `Opponent()`. Siempre derivado de la conexión. |
| `TickId` | `long` | Simulation | `Zero` = estado inicial. `Next()`, `Plus(n)`, diferencia en ticks, comparaciones. |
| `CommandSequence` | `long` | Command Admission | Orden global de admisión; también prioridad de Production. |
| `ClientCommandId` | `long` | Cliente | **DECIDIDO (D-005):** rango [1, 2^53]; Networking valida con `IsInValidRange`. |
| `IdempotencyKey` | `PlayerId` + `ClientCommandId` | — | Clave de idempotencia aprobada. |
| `OrderId` | `long` | Simulation | `0` = sin orden. Repath con `OrderId` viejo se rechaza. |
| `MapRevision` | `long` | Simulation | `Initial` = 0; ordenable. |
| `ConnectionId` | `long` | Networking | Sin referencias a sockets. |
| `SessionId` | `Guid` | GameSession | RECOMENDADO (D-010). |
| `InitializationId` | `long` | GameSession | RECOMENDADO (D-010): contador creciente por intento de inicio. |

## 4. Geometría y grilla

**DECIDIDO (D-004, D-006):**

- Posiciones oficiales `PositionXZ(double X, double Z)` en unidades de mundo. La altura de Godot no es autoridad.
- `GridSpec(CellSize, Columns, Rows)`: el mapa cubre X ∈ [0, Columns·CellSize) y Z ∈ [0, Rows·CellSize), **intervalos semiabiertos**.
- Celda de una posición: `(floor(X / CellSize), floor(Z / CellSize))`. Una posición dentro del mapa siempre cae en una celda válida, incluso con redondeo en el borde.
- **Ocupación de una unidad:** la celda que contiene su punto, sin radio. Es la base de "no colocar sobre una unidad presente" (03 §2) y de "celda de spawn libre de unidades" (05 §5).
- `Footprint(Anchor, FootprintSize(W, H))`: rectángulo de columnas [Anchor.Column, Anchor.Column + W) y filas [Anchor.Row, Anchor.Row + H), sin rotación. `Cells()` las recorre en orden fila ascendente y luego columna ascendente.

**Distancias (DECIDIDO D-019):** `GridSpec.ToWorldRect(footprint)` da la extensión de una huella en coordenadas de mundo. `WorldRect.DistanceTo` mide al borde más cercano, y vale 0 si el punto está dentro. `PositionXZ.IsWithinRange` y `WorldRect.IsWithinRange` comparan de forma **inclusiva** usando distancias al cuadrado. Combat y Movement usan estas mismas primitivas.

**Coordenadas con Godot (DECIDIDO D-029):** son idénticas. Servidor X/Z = Godot X/Z, se ignora Godot Y, 1 unidad = 1 metro, y el mapa ocupa X y Z positivos con la esquina (0,0) en el origen.

**PENDIENTE:** escala concreta del mapa, valores de `CellSize` y tamaño del mapa, tamaño de huella por tipo, tolerancias de distancia y rango (módulos 02-04).

## 5. Comandos y resultados

- `CommandEnvelope(Sequence, Player, ClientCommandId, Command)`: comando ya admitido. `Key` devuelve la `IdempotencyKey`.
- `IGameCommand`: payload tipado con `Type`. **Nunca incluye `PlayerId`**. RECOMENDADO (D-009): los payloads concretos (`MoveCommand`, `BuildCommand`, …) se agregan en `Rts.Shared.Commands` al trabajar cada módulo.
- `CommandType`: `Move`, `Stop`, `Attack`, `GatherWood`, `Build`, `RepairFarm`, `TrainUnit`, `RepathResponse` (D-028).
- Payloads ya definidos: `MoveCommand`, `StopCommand`, `AttackCommand`, `GatherWoodCommand`, `BuildCommand` (formas `Place` y `Continue`, D-032), `RepairFarmCommand` y `RepathResponseCommand`, con una `UnitRouteCandidate` por unidad (ver contratos [03](03-construction.md) y [04](04-units-movement.md)), y `TrainUnitCommand` (ver [contrato 05](05-production.md)).
- `CommandResult(Key, Sequence, Tick, Outcome, Error?, EntityResults)`: el resultado oficial que produce Simulation y se reutiliza para responder duplicados. Invariantes que verifica el constructor:

| Outcome | Error global | Resultados por entidad |
|---|---|---|
| `Accepted` | nulo | ninguno rechazado |
| `PartiallyAccepted` | nulo | al menos uno aceptado y uno rechazado |
| `Rejected` | obligatorio | ninguno aceptado (si es grupal y todos fallan: `Common.NoEntityAccepted`) |

DECIDIDO (D-043, resuelve O-004): el registro de idempotencia vive en Simulation y se conserva toda la partida (ver [contrato 06 §3](06-simulation.md#3-idempotencia-decidido-d-043-resuelve-o-004)).

## 6. Eventos de dominio y colecciones

- `DomainEvent(EventStamp Stamp, IDomainEventPayload Payload)`: **solo Simulation lo crea, y solo después del commit**. Los módulos de dominio proponen payloads sin sello.
- `EventStamp(TickId Tick, int IndexInTick)`: identidad y orden oficial; se ordena por tick y luego por índice.
- La visibilidad (ambos / solo dueño / solo emisor) **no** forma parte del evento: la decide State Sync (09 §9).
- RECOMENDADO (D-011): las colecciones dentro de contratos usan `EquatableArray<T>`, que tiene igualdad estructural y admite expresiones de colección `[a, b]`. Así dos resultados con igual contenido son iguales, lo que habilita las pruebas de determinismo exigidas (Economy/Combat en distinto orden ⇒ resultado idéntico).

## 7. Errores

**DECIDIDO (D-007):** `ErrorCode` es un `readonly record struct` sobre un string con formato `Area.Nombre` en PascalCase, validado en el constructor. En JSON se publica como string.

Transversales (`CommonErrors`): `Common.InvalidCommand`, `Common.NotOwner`, `Common.EntityNotFound`, `Common.IncompatibleEntity`, `Common.MatchNotRunning`, `Common.NoEntityAccepted`.

Cada módulo declara sus códigos en su proyecto con su prefijo de área (p. ej. `Economy.InsufficientWood`, `Movement.InvalidPath`, `Production.QueueFull`). **PENDIENTE:** taxonomía exhaustiva.

## 8. Recursos, entidades y tiempo

- `ResourceAmount(int Wood, int Food)`: saldos, costos y deltas. La aritmética usa `checked`, de modo que un desbordamiento lanza una excepción. La asequibilidad (`CanAfford`) pertenece a Economy.
- `UnitType { Worker, Soldier }`, `BuildingType { MainCenter, Farm, Barracks, Tower, Fence }`, `EntityKind { Unit, Building, Tree }`. El valor `0` queda sin definir a propósito, para detectar valores no inicializados.
- `TickRate`: `Default` = 20 Hz (50 ms). `PerTick(perSecond)` convierte velocidades. RECOMENDADO (O-007): usarlo en lugar de la constante 20.

## 9. Pendientes de este contrato

- Payloads concretos de comandos y eventos: se agregan módulo por módulo (D-009).
- ~~Tipos de resultado de partida~~: definidos en `Rts.Shared.Match` (contratos [06](06-simulation.md) y [07](07-game-session.md)).
- DTOs JSON y serialización: viven en `Rts.Protocol` (D-066), no en este núcleo. Definidos en los contratos [08](08-networking.md) (sobre, sesión, transporte y comandos) y [09](09-state-sync.md) (estado).
