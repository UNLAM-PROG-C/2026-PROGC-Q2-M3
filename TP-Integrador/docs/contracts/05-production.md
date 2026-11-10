# Contrato 05 — Production (`Rts.Production`)

> **Estado:** contratos, modelos, interfaces y configuración implementados (`server/src/Rts.Production`) y probados (`server/tests/Rts.Production.Tests`). **La lógica de cálculo y de búsqueda de celda de aparición todavía no está implementada.**
> **Etiquetas:** **DECIDIDO** = aprobado (módulo [05-production](../modules/05-production.md) o [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Depende de:** [Contrato 00](00-shared-kernel.md). No depende de ningún otro módulo: recibe la comida y los costos de Economy como datos inyectados.

## 1. Superficie pública

| Tipo | Namespace | Función |
|---|---|---|
| `IProductionCalculator.CalculateTick(ProductionTickInput) → ProductionTickResult` | `Rts.Production` | Progreso, apariciones e inicios (fase 8) |
| `IProductionRules` | `Rts.Production` | `EvaluateTrainRequest` (fase 1), `FindSpawnCell`, `GetTrainingDurationTicks` |
| `ProductionTickInput`, `PlayerFood`, `ProductionTickResult`, `ProductionStart`, `SpawnProposal` | `Rts.Production.Contracts` | Entradas y salidas inmutables |
| `ProducerState`, `ProductionRequest`, `ProductionJob`, `SpawnView`, `ProducerInfo`, `ProducerCatalog` | `Rts.Production.Model` | Modelos |
| `ProductionConfig` | `Rts.Production.Configuration` | Parámetros tipados y validación |
| `ProductionErrors` | `Rts.Production` | `NotAProducer`, `WrongUnitType`, `ProducerNotOperational`, `QueueFull` |
| `TrainUnitCommand` | `Rts.Shared.Commands` | Payload del comando |
| `ProducerStatus` | `Rts.Shared.Entities` | `Idle`, `WaitingForResources`, `Training`, `PendingSpawn` |
| `UnitSpawned`, `ProductionChanged` | `Rts.Shared.Events` | Eventos |

## 2. Propiedad de datos

| Dato | Dueño | Notas |
|---|---|---|
| Cola, tarea activa y estado de cada productor | Production calcula / Simulation confirma | `ProducerState` |
| Qué entrena cada edificio | Production (`ProducerCatalog`, DECIDIDO 05 §1) | Centro Principal → Trabajador; Cuartel → Soldado |
| Duraciones, capacidad de cola y radio de spawn | Production (`ProductionConfig`) | |
| Costo en comida por unidad | Economy (D-015) | Se inyecta en la implementación |
| Saldo de comida | Simulation (`WorldState`) | Entra como `PlayerFood` |
| HP máximo de la unidad nueva | Combat (D-021) | Lo aplica Simulation al crearla |
| ID de la unidad nueva | Simulation | Emite `UnitSpawned` |

Una solicitud se identifica por la `Sequence` global de su comando `TrainUnit`; no hay un `RequestId` aparte (RECOMENDADO D-041).

## 3. TrainUnit en la fase 1

`EvaluateTrainRequest(requester, unitType, producer)` verifica, en este orden:

| Verificación | Error |
|---|---|
| El productor existe | `Common.EntityNotFound` |
| Es del jugador | `Common.NotOwner` |
| Es un edificio productor | `Production.NotAProducer` |
| Entrena ese tipo de unidad | `Production.WrongUnitType` |
| Está completado y operativo en este tick (un Cuartel completado en N opera desde N+1) | `Production.ProducerNotOperational` |
| Su cola pendiente tiene lugar (`PendingCount < MaxPendingPerProducer`) | `Production.QueueFull` |

Si todo pasa, Simulation agrega `ProductionRequest(Sequence, producer, owner, unitType)` al final de la cola y emite `ProductionChanged`. **No** se exige comida (05 §2). Los duplicados por `(PlayerId, ClientCommandId)` los descarta la idempotencia de Simulation antes de llegar aquí.

## 4. Reglas de la fase 8 que debe cumplir la implementación

La fase 8 solo corre si la partida sigue tras Victory Rules (05 §6). Solo actúan los productores con `IsOperationalAt(Tick)`. Se procede en este orden:

1. **Libres al entrar:** se anotan los productores sin tarea activa. Son los únicos que pueden iniciar en esta fase (05 §4).
2. **Progreso (DECIDIDO D-037):** toda tarea con `RemainingTicks > 0` resta 1. Como el progreso ocurre antes de los inicios, una tarea iniciada en N avanza por primera vez en N+1. Con duración D, termina y aparece en **N+D**.
3. **Apariciones:** las tareas con `RemainingTicks == 0`, recién terminadas o en `PendingSpawn` de antes, se procesan por **`Sequence` ascendente** (RECOMENDADO D-041):
   - se busca celda con `FindSpawnCell`, salteando las ya reservadas en esta fase;
   - si hay celda, se propone `SpawnProposal`, se reserva la celda y se libera la tarea activa (pero ese productor **no** inicia otra en esta fase);
   - si no hay celda, la tarea queda `PendingSpawn` y no se vuelve a cobrar.
4. **Inicios (DECIDIDO D-038, prioridad estricta):**
   - Los candidatos son las cabezas de cola de los productores libres al entrar, ordenados por `Sequence` ascendente.
   - Se lleva la comida disponible de cada jugador. Para cada candidato: si el costo alcanza, se propone `ProductionStart` con `RemainingTicks = D`, `PaidFood = costo` y `StartedAtTick = Tick`, y se descuenta la comida localmente.
   - Si **no** alcanza, ese jugador queda bloqueado por el resto de la fase: ninguna solicitud posterior suya inicia, aunque sea más barata. La más antigua nunca queda postergada.
   - Los jugadores son independientes, porque cada uno tiene su propia comida.
5. **Eventos:** `ProductionChanged` por cada productor cuyo estado discreto cambió, ordenados por `ProducerId`. No se emite en cada tick de progreso (RECOMENDADO D-042).
6. Las colecciones del resultado no dependen del orden de las colecciones de entrada.

### Búsqueda de celda de aparición (`FindSpawnCell`, DECIDIDO D-039, D-040)

- Se recorren anillos de celdas alrededor de la huella, de `r = 1` a `SpawnSearchRadiusCells`. El anillo r son las celdas a distancia de Chebyshev exactamente r del rectángulo de la huella.
- Dentro de cada anillo, primero la celda cuyo centro está **más cerca del centro de la huella**, con distancia euclídea al cuadrado y en coordenadas de mundo. Los empates se resuelven por `(Row, Column)` ascendente.
- Una celda es apta si:
  - está dentro del mapa;
  - no tiene estructura;
  - **no está reservada por un árbol** (D-040);
  - no contiene el punto de ninguna unidad (D-004);
  - no fue reservada por otra aparición del mismo tick.
- La posición de la unidad nueva es el **centro de la celda**.

## 5. Lo que Simulation debe hacer con el resultado (commit de la fase 8)

- Para cada `ProductionStart`, en orden: confirmar con `IEconomyRules.EvaluateSpend` y, en un único commit atómico, descontar `PaidFood` y activar la tarea (05 §3). Si la verificación fallara, sería una inconsistencia crítica: no se aplica a medias (06 §5).
- Para cada `SpawnProposal`, en orden:
  - crear la unidad con un `EntityId` nuevo, HP = `ICombatRules.GetUnitMaxHp`, la posición dada y orden Idle;
  - si es soldado, `ReadyAtTick = Tick` (contrato 02 §5);
  - emitir `UnitSpawned` (a ambos), que precede a cualquier posición publicada de esa unidad.
- Aplicar `ProducerUpdates` y sellar `ProductionChanged`, que va **solo al dueño**.
- Al destruirse un productor en Combat: descartar su estado (tarea y cola) sin reembolso (05 §6, D-025). Como el productor ya no está en la entrada, no actúa en la fase 8 de ese tick.

## 6. Configuración (`ProductionConfig`)

| Parámetro | Validación | Valor |
|---|---|---|
| `TrainingDurationTicks` (Worker, Soldier) | > 0 | PENDIENTE |
| `MaxPendingPerProducer` | > 0 | PENDIENTE |
| `SpawnSearchRadiusCells` | > 0 (anillos de celdas) | PENDIENTE |

Los costos en comida (3 y 5 fueron ejemplos ilustrativos) viven en `EconomyConfig.UnitFoodCosts`.

## 7. Pruebas de aceptación para la etapa de implementación

Estas pruebas de 05 §9 se escriben al implementar:

- combinaciones de productor y unidad aceptadas y rechazadas;
- cola llena;
- solicitud sin comida que queda pendiente;
- dos productores que compiten y cobran al más antiguo, con prioridad estricta;
- inicio que cobra exactamente una vez;
- duración D que aparece en N+D;
- spawn en la celda más cercana y con la primera ocupada salteada;
- dos spawns simultáneos que no comparten celda;
- radio completo bloqueado ⇒ `PendingSpawn`, y al liberarse aparece sin cobrar de nuevo;
- la siguiente tarea solo inicia en el tick posterior al spawn;
- edificio recién completado que espera a N+1;
- celdas de árbol salteadas;
- resultados idénticos al permutar las entradas.

Son de integración con Simulation: productor destruido que pierde cola y tarea, duplicado de solicitud y fin de partida que evita Production.

## 8. Pendientes

- Valores (§6).
- Política de publicación del progreso en Godot (State Sync).
