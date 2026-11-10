# Contrato 06 — Simulation (`Rts.Simulation`)

> **Estado:** modelo del mundo, interfaces, configuración, mapa y métricas implementados (`server/src/Rts.Simulation`) y probados (`server/tests/Rts.Simulation.Tests`). **El motor de ticks, el loop y la cola de comandos todavía no están implementados.**
> **Etiquetas:** **DECIDIDO** = aprobado (módulo [06-simulation](../modules/06-simulation.md) o [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Depende de:** [Contrato 00](00-shared-kernel.md) y de los contratos de dominio [01](01-economy.md), [02](02-combat.md), [03](03-construction.md), [04](04-units-movement.md) y [05](05-production.md). Ningún módulo de dominio depende de Simulation.

## 1. Superficie pública

| Tipo | Namespace | Función |
|---|---|---|
| `ISimulationEngine` (`InitializeMatch`, `AdvanceTick`, `Current`) | `Rts.Simulation` | Núcleo sincrónico: un tick completo por llamada |
| `ISimulationLoop` : `ISimulationControl` | `Rts.Simulation` | Loop a 20 Hz en un hilo dedicado; control para GameSession |
| `TickOutcome` | `Rts.Simulation` | Publicación, resultado de gameplay o falla, y métricas |
| `WorldState`, `UnitEntity`, `BuildingEntity`, `TreeEntity`, `PlayerState`, `IdempotencyLedger` | `Rts.Simulation.State` | Mundo autoritativo **inmutable** (D-044) |
| `MapDefinition`, `PlayerStartDefinition`, `InitialUnitDefinition` | `Rts.Simulation.Map` | Mapa fijo cargado desde archivo (D-046) |
| `SimulationConfig`, `DiagnosticsLevel` | `Rts.Simulation.Configuration` | Parámetros |
| `SimulationPhase`, `PhaseTiming`, `TickMetrics`, `SimulationFailure` | `Rts.Simulation.Diagnostics` | Métricas y fallas |

Tipos de integración en `Rts.Shared`, que usan módulos que no pueden referenciar Simulation (D-002, D-049):

| Tipo | Namespace | Uso |
|---|---|---|
| `ICommandSink`, `ICommandSource`, `CommandAdmission` | `Rts.Shared.Commands` | Cola global: Networking admite y Simulation consume |
| `ISimulationControl`, `IMatchResultSink`, `MatchResult`, `MatchResultType`, `MatchEndReason` | `Rts.Shared.Match` | Simulation ↔ GameSession |
| `WorldView` (y sus vistas), `UnitPosition`, `TickPublication`, `ITickPublisher` | `Rts.Shared.World` | Simulation → State Sync |
| `ResourcesChanged` | `Rts.Shared.Events` | Saldo al cierre del tick (solo dueño) |

## 2. Modelo del mundo (DECIDIDO D-044: inmutable)

- `WorldState` es un record inmutable. Sus colecciones son `ImmutableSortedDictionary` ordenados por `EntityId` (o `PlayerId`), así que todo recorrido es determinista.
- Cada paso del tick construye un `WorldState` nuevo a partir del anterior. Solo el hilo de Simulation construye estados y reemplaza `Current`.
- Las entidades agregan las vistas que usa cada módulo, para no duplicar datos:
  - `UnitEntity`: Movement (orden, ruta, repath), Economy (`GatherTicksAccumulated`) y Combat (`Hp`, `ReadyAtTick`).
  - `BuildingEntity`: guarda directamente los modelos de los módulos (`FarmEconomyState`, `FarmRepairState`, `ProducerState`) y el cooldown de torre. Sus constructores verifican qué partes corresponden a cada tipo y estado.
  - `TreeEntity`: reserva una celda y su posición es el centro de esa celda (RECOMENDADO D-050).
- `LastEntityId` y `LastOrderId` son los contadores de IDs del servidor.
- `IdempotencyLedger` es el registro de idempotencia (D-043).
- `GameplayResult` lleva el resultado fijado por Victory Rules.

## 3. Idempotencia (DECIDIDO D-043, resuelve O-004)

- Command Admission **no** detecta duplicados: todo comando estructuralmente válido entra a la cola con su `Sequence`.
- En la fase 1, antes de procesar cada comando, Simulation busca su `IdempotencyKey` en el `IdempotencyLedger`:
  - si ya existe, **no lo ejecuta** y vuelve a publicar el `CommandResult` original para el emisor;
  - si no existe, lo procesa y registra el resultado.
- Como la cola es FIFO, el original siempre se procesa antes que sus duplicados.
- La clave decide sola: un duplicado con otro payload igual se trata como duplicado.
- El registro se conserva durante toda la partida y solo lo toca el hilo de Simulation, sin locks.

## 4. El tick, paso a paso (`AdvanceTick`)

Sea `S` el último `WorldState` confirmado y `W` el estado de trabajo, inicialmente `W = S`.

| Fase | Qué hace | Contrato |
|---|---|---|
| **1. Lote de comandos** | `ICommandSource.DequeueBatch(MaxCommandsPerTick)`. Por cada comando, en orden de `Sequence`: aplica la idempotencia (§3); si es nuevo, lo valida y aplica como **un commit atómico propio**. Si se rechaza, `W` no cambia y las unidades conservan su orden. Produce el `CommandResult` y sus eventos | 03 §3-4, 04 §3, 05 §3, 02 §6 |
| **2. Movement** | Arma `MovementTickInput` desde `W` (mapa con la `MapRevision` vigente), llama a `CalculateTick` y aplica `UnitUpdates` | 04 §5-6 |
| **3. Construction** | Filtra constructores y reparadores en rango (`IsWithinInteractionRange` con `BuildRange`), llama a `CalculateTick` y aplica progreso, finalizaciones y su cascada | 03 §5-6 |
| **4. Snapshot** | `snapshot = W`, la misma referencia. Es inmutable por tipo; no hay copia | D-044 |
| **5. Economy ∥ Combat** | Arma `EconomyTickInput` (recolectores en rango con `GatherRange`) y `CombatTickInput` desde `snapshot`. Los ejecuta en **tareas paralelas** (o en secuencia si `RunDomainPhaseInParallel = false`, D-051) | 01 §3, 02 §3 |
| **6. Barrera + commit** | Espera **ambos** resultados con `BarrierTimeout`. Valida coherencia: mismo tick y referencias existentes en `snapshot`. Aplica **primero Economy y después Combat**, y luego la cascada de destrucciones | 01 §5, 02 §5, 03 §6, 05 §5 |
| **7. Victory Rules** | Si en la fase 6 cayó un solo Centro Principal ⇒ `BaseDestroyed` y gana el otro; si cayeron ambos ⇒ `Draw`. Fija `GameplayResult` | 02 §5, 06 §3 |
| **8. Production** | Solo si no hay resultado. Arma `ProductionTickInput` (comida tras la fase 6), llama a `CalculateTick` y confirma inicios y apariciones en orden | 05 §4-5 |
| **9. Publicación** | Agrega `ResourcesChanged` de cada jugador cuyo saldo cambió respecto de `S`. Arma `TickPublication` con los eventos, los `CommandResult` y las posiciones. **Reemplaza `Current = W`** y después llama a `ITickPublisher.Publish` | 06 §9 |

**Atomicidad del tick (consecuencia de D-044):**

- `Current` solo cambia al final de la fase 9.
- Si cualquier fase lanza una excepción, la barrera excede el plazo o un resultado es incoherente, el tick se **descarta completo**: `Current` sigue siendo `S`, no se publica nada y `TickOutcome.Failure` informa la fase.
- El loop avisa `MatchResult.CriticalError` a GameSession y no ejecuta más ticks (06 §5, §7).
- Esto resuelve el PENDIENTE de 06 §5: no hay medias mutaciones posibles, porque nunca se escribe sobre `S`.

**Determinismo (06 §6):**

- Los comandos se procesan por `Sequence` y las entidades por ID.
- Economy y Combat solo leen `snapshot`, y se aplican en orden fijo, sin importar cuál termina primero.
- Mismas entradas y mismo orden de comandos ⇒ mismo `WorldState` y mismos eventos.

## 5. Orden oficial de eventos dentro del tick (RECOMENDADO D-048)

`EventStamp.IndexInTick` se asigna en este orden:

1. Fase 1, por `Sequence` de comando. Dentro de un comando, en orden de emisión: `BuildingPlaced` → `MapRevisionChanged` → `UnitOrderChanged`, o `FarmRepairStarted`, o `ProductionChanged`.
2. Fase 2: eventos de Movement por `UnitId`.
3. Fase 3: eventos de Construction y su cascada (`UnitOrderChanged` de constructores que pasan a Idle).
4. Fase 6: eventos de **Economy**, después los de **Combat**, y al final la cascada:
   - `UnitOrderChanged` de las cancelaciones, por `UnitId`;
   - `MapRevisionChanged`, si algún edificio fue destruido.

   El orden Economy → Combat refleja la regla aprobada: "la comida se acredita y después se destruye" (01 §4).
5. Fase 8: `UnitSpawned` en el orden de aparición, y después `ProductionChanged` por `ProducerId`.
6. Cierre: `ResourcesChanged` de P1 y después de P2, una vez por jugador y solo si su saldo cambió en el tick.

`MatchEnded` **no** es un evento de mundo: GameSession lo emite después de recibir el resultado, y State Sync lo ordena después del último lote de gameplay.

## 6. Loop (`ISimulationLoop`)

- Un hilo dedicado llama a `AdvanceTick` cada `TickRate.TickInterval` (50 ms), con reloj monotónico. Nunca hay dos ticks a la vez.
- **Sobrecarga (DECIDIDO D-047):** si un tick se pasa del intervalo, se registra `IsOverrun`, el próximo arranca de inmediato y el reloj se **re-ancla**. No hay ráfagas de recuperación.
- **Supervisión (06 §7):** si un tick queda bloqueado más allá de un plazo de seguridad, se informa una falla crítica desde fuera del hilo del tick.
- `StartTicks` se llama al pasar a Running. `StopTicksAsync` termina el tick en curso, si lo hay, y detiene el loop; es idempotente. Devuelve el último tick confirmado (`TickId.Zero` si no corrió ninguno), que GameSession usa como `FinalTick` de un resultado por desconexión (RECOMENDADO D-061).
- Ante `GameplayResult` o `Failure`, el loop llama a `IMatchResultSink.OnSimulationResult` y deja de ejecutar ticks.
- **Orden exigido por D-055:** si el tick en curso al pedir la detención produce un resultado, el loop publica ese tick y llama al sink **antes** de completar `StopTicksAsync`. Así el resultado del mundo llega a GameSession antes que el aviso de detención ([contrato 07 §6](07-game-session.md#6-desconexiones-en-running-lote-y-precedencia)).
- En los estados WaitingForPlayers y Starting no corren ticks (06 §2).

## 7. Cola global de comandos

- `ICommandSink.Admit` lo llaman los receptores concurrentes de Networking. Asigna `Sequence` y encola en **una sola operación atómica**, con capacidad finita `CommandQueueCapacity`. Si la cola está llena, devuelve `Common.CommandQueueFull` y no descarta nada de lo ya admitido.
- `ICommandSource.DequeueBatch` lo llama solo el hilo de Simulation.
- La implementación vivirá en Simulation y la composición del servidor la inyectará en Networking como `ICommandSink`.
- Los comandos de gameplay recibidos fuera de Running los rechaza Networking (consultando la sesión) antes de admitirlos, con `Common.MatchNotRunning`.

## 8. Inicialización (`InitializeMatch`) y mapa

DECIDIDO (D-046): el mapa sale de un **archivo del servidor**, que es la única fuente de verdad. Godot construye el layout jugable (límites, árboles, edificios y unidades iniciales) a partir del `InitialState`; su escena aporta el terreno plano y los visuales.

`InitializeMatch` crea el `WorldState` del tick 0:

- grilla del mapa y `MapRevision.Initial`;
- un árbol por celda de `TreeCells`, con `MaxWoodPerTree`;
- por jugador: su Centro Principal completado (`OperationalFromTick = 0`, HP máximo de Construction, productor vacío), sus unidades iniciales (HP máximo de Combat, Idle) y `InitialBalance` de Economy;
- IDs asignados en orden determinista: árboles, después P1 y después P2.

Devuelve `WorldView`. Puede repetirse en una nueva preparación de Starting (07 §4).

Formato del archivo (RECOMENDADO D-052; el cargador se implementará con el motor):

```json
{
  "cellSize": 1.0,
  "columns": 64,
  "rows": 64,
  "trees": [ { "column": 10, "row": 20 }, { "column": 53, "row": 43 } ],
  "starts": [
    { "player": 1, "mainCenterAnchor": { "column": 4, "row": 4 },
      "initialUnits": [ { "type": "Worker", "x": 9.5, "z": 5.5 } ] },
    { "player": 2, "mainCenterAnchor": { "column": 56, "row": 56 },
      "initialUnits": [ { "type": "Worker", "x": 54.5, "z": 58.5 } ] }
  ]
}
```

`MapDefinition.GetValidationErrors(huellaDelCentroPrincipal)` se ejecuta al iniciar el servidor. Verifica límites, árboles duplicados, un inicio por jugador, Centros Principales sin superposición y unidades fuera de las huellas. La simetría la garantiza el diseño del archivo.

## 9. Configuración (`SimulationConfig`)

| Parámetro | Validación | Valor |
|---|---|---|
| `TickRate` | > 0 | **20 Hz** (DECIDIDO) |
| `MaxCommandsPerTick` | > 0 | PENDIENTE |
| `CommandQueueCapacity` | ≥ `MaxCommandsPerTick` | PENDIENTE |
| `BarrierTimeout` | > intervalo de tick (holgado) | PENDIENTE |
| `RunDomainPhaseInParallel` | — | `true` (D-051: `false` solo para medir) |
| `Diagnostics` | `Summary` / `Debug` | `Summary` |

Validación cruzada en la composición del servidor: `MovementConfig.AttackRange == CombatConfig.Soldier.Range` (D-030).

## 10. Métricas (06 §10)

`TickMetrics` por tick:

- duración total y por fase (`PhaseTiming` para las 9 fases);
- espera en la barrera;
- comandos tomados, aceptados, rechazados y duplicados respondidos;
- eventos publicados;
- si hubo sobrecarga.

En `Summary` se registran resúmenes y errores; en `Debug`, el detalle por tick. Comparar `RunDomainPhaseInParallel` en `true` y en `false` sirve para la defensa del TP.

## 11. Pruebas para la etapa de implementación

Estas pruebas de 06 §10 se escriben al implementar:

- comandos simultáneos sobre la misma celda;
- lote de 100 con límite 30 que deja 70 pendientes;
- duplicados sin doble gasto y con respuesta original;
- grupo aceptado en parte;
- Build fallido sin descuento;
- Economy y Combat terminando en distinto orden con resultado idéntico, también comparando paralelo frente a secuencial;
- trabajador que recolecta y muere en el mismo tick;
- granja que produce y es destruida en el mismo tick;
- ataques mutuos;
- ambas bases caen ⇒ `Draw`;
- Combat que falla antes de la barrera ⇒ tick descartado completo;
- productor destruido que no inicia entrenamientos;
- sobrecarga sin ticks solapados;
- ningún resultado se confirma dos veces;
- estado inicial solo en Starting;
- ningún tick después de terminada la partida.

## 12. Pendientes

- Implementación del motor, el loop, la cola y el cargador de mapa.
- Valores (§9) y diseño del mapa simétrico real.
- ~~Precedencia fina entre el resultado de Simulation y desconexiones casi simultáneas.~~ Resuelta por D-055 (ver [contrato 07 §6](07-game-session.md#6-desconexiones-en-running-lote-y-precedencia)).
