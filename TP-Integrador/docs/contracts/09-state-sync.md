# Contrato 09 — State Sync

> **Estado:** primera versión de contratos implementada (`server/src/Rts.StateSync`, DTOs en `server/src/Rts.Protocol`) y probada (`server/tests/Rts.StateSync.Tests`, `server/tests/Rts.Protocol.Tests`). El proyector y el publicador **todavía no tienen lógica**.
> **Etiquetas:** **DECIDIDO** = aprobado por el equipo (ver [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Módulo:** [09 — State Sync](../modules/09-state-sync.md). **Vecinos:** [06 Simulation](06-simulation.md), [07 GameSession](07-game-session.md), [08 Networking](../modules/08-networking.md).

## 1. Superficie pública y dependencias

**DECIDIDO (D-066):** los DTOs JSON viven en un proyecto propio, **`Rts.Protocol`**: es el esquema único que comparten State Sync, Networking y Godot. Referencia `Rts.Shared` solo por dos cosas: los enums de dominio, que se serializan como string, y `PlayerId`, para dirigir mensajes. Los IDs de los DTOs son tipos primitivos y ningún DTO expone una entidad de dominio (08 §3).

| Proyecto | Referencia | Contenido |
|---|---|---|
| `Rts.Protocol` | `Rts.Shared` | `ProtocolJson.Options`; DTOs de estado (`InitialStateDto`, `StateEventsDto` y eventos, `PositionUpdateDto`); `OutboundMessage`, `DeliveryClass`, `IClientOutbox`, `ProtocolMessageTypes` |
| `Rts.StateSync` | `Rts.Shared`, `Rts.Protocol` | `StateSyncConfig`, `EventAudience`, `IEventAudiencePolicy`, `IStateProjector`, `InitialStateRequest`, `TickProjection`, `IStateSyncPublisher` |
| `Rts.Shared` (agregados) | — | `GameCatalog` y sus entradas; `ISessionGate.GetConnection`; `Owner` en `ProductionChanged` y `RepathRequested` |

`IStateProjector` es **puro y sin estado**: la proyección depende solo de la publicación y de la configuración. `IStateSyncPublisher` lo usa y encola el resultado.

## 2. Flujo de publicación

**DECIDIDO (D-064): proyección sincrónica.**

```
Simulation (fase 9, su hilo)            Networking (un escritor por conexión)
  Current = W
  ITickPublisher.Publish(pub) ──► IStateSyncPublisher
                                    ├─ IStateProjector.ProjectTick(pub)   ← puro, sin I/O
                                    └─ IClientOutbox.Enqueue(P1, …)  ──► cola P1 ──► SendLoop P1 ──► socket
                                       IClientOutbox.Enqueue(P2, …)  ──► cola P2 ──► SendLoop P2 ──► socket
```

- Todo ocurre dentro de `Publish`, en el hilo de Simulation. Se filtra, se arman DTOs y se encolan. **No hay I/O, esperas ni colas propias**; la serialización a bytes ocurre en el escritor de cada conexión.
- Al volver `Publish`, los mensajes del tick ya están en las colas de salida, en orden. Como GameSession fija el resultado con los ticks detenidos (D-055), su `MatchEnded` queda **siempre detrás** del último lote. Con eso se resuelve el PENDIENTE "contrato de publicación entre GameSession y State Sync" (09 §13): `MatchStarted` y `MatchEnded` los envía GameSession por su propio camino (contrato 07) y el orden queda garantizado por construcción.
- `PublishInitialState` se llama desde el host de GameSession, durante Starting. Nunca coincide con `Publish`, porque en Starting no corren ticks.
- Un cliente lento no afecta a nadie. `Enqueue` no bloquea, y la saturación la resuelve Networking desconectando (09 §7, 08 §5).

## 3. Visibilidad

La decide `IEventAudiencePolicy` **mirando solo el evento**. Los eventos privados traen su dueño (DECIDIDO D-063), así que State Sync no mantiene un índice de entidades. Un evento de tipo desconocido hace fallar la política, para que nunca se publique por descuido.

| Evento de dominio | `type` del DTO | Destinatarios |
|---|---|---|
| `BuildingPlaced`, `BuildingProgressChanged`, `BuildingCompleted` | igual | Ambos |
| `FarmDepleted`, `FarmRepairStarted`, `FarmRepairProgressChanged`, `FarmRepairCompleted` | igual | Ambos (es el `FarmStateChanged` conceptual de 09 §4) |
| `MapRevisionChanged` | igual | Ambos |
| `EntityDamaged`, `EntityDestroyed` | igual | Ambos |
| `TreeDepleted`, `TreeRespawned` | igual | Ambos |
| `UnitSpawned` | igual | Ambos |
| `UnitOrderChanged` | igual | **Ambos** (DECIDIDO D-067: no hay niebla de guerra y el rival anima las acciones) |
| `RepathRequested` | igual | **Solo `Owner`** (D-028) |
| `ProductionChanged` | igual | **Solo `Owner`** (09 §4) |
| `ResourcesChanged` | igual, sin `PlayerId` | **Solo `Player`** (09 §4) |
| `CommandResult` | en `commandResults` del lote | **Solo el emisor** (`Key.Player`) |

**InitialState:** todo lo público para ambos. `ownResources` y `ownProduction` llevan **solo** los datos del destinatario. Nunca se exponen saldos ni colas rivales (09 §10).

## 4. InitialState

`IStateProjector.BuildInitialState(InitialStateRequest)` produce un `InitialStateDto` por destinatario:

| Campo | Origen |
|---|---|
| `sessionId`, `initializationId`, `playerId`, `tick` | sesión, inicialización, destinatario, `WorldView.Tick` |
| `map` | `WorldView.Map`: tamaño de celda, columnas, filas y revisión |
| `players` | para cada jugador, su ID y su Centro Principal; nada privado |
| `units`, `buildings`, `trees` | vistas públicas del `WorldView` |
| `ownResources`, `ownProduction` | `PlayerView` y `ProducerView` del destinatario |
| `catalog` | `GameCatalog` |

- **DECIDIDO (D-068):** el InitialState trae un **catálogo** público, igual para ambos: HP máximo y huella por tipo, costos, duración de entrenamiento, velocidad, ataques, costo y trabajo de reparación, rangos de interacción y tick rate. Godot no duplica valores de balance.
- **RECOMENDADO (D-071):** la composición del servidor (`Rts.Server`) arma el `GameCatalog` una vez, desde las configuraciones ya validadas de cada módulo, y lo inyecta en State Sync. `GameCatalog` vive en `Rts.Shared.World` porque State Sync no puede referenciar los módulos de dominio.
- Para cada cliente, el InitialState se encola después de `PlayerAssigned`, porque el host de GameSession ejecuta sus acciones en orden (contrato 07 §5).

## 5. Lotes de eventos (`StateEvents`)

**RECOMENDADO (D-070):**

- **Un `StateEventsDto` por tick y destinatario**, con `tick`, `mapRevision`, los eventos visibles en el **orden oficial** (contrato 06 §5) y los resultados de los comandos de ese destinatario procesados en el tick, incluidas las respuestas a duplicados.
- Cada evento lleva `index` = su `IndexInTick`. Puede tener huecos, porque cada destinatario ve un subconjunto, pero junto con `tick` identifica el evento de forma estable (09 §4).
- **Si no hay eventos ni resultados para ese destinatario, no se envía el lote** (09 §2 lo permite). El avance del tiempo lo marcan las posiciones.
- **Mapeo 1:1** con los eventos de dominio. El nombre del evento es el discriminador `type`. Así no hay que traducir nombres ni fusionar eventos: los cuatro eventos de granja reemplazan al `FarmStateChanged` conceptual.

**DECIDIDO (D-065, resuelve O-009):** el progreso y el daño a una obra se publican **separados y sin agrupar**. Un DTO por evento, en el orden oficial: el progreso es de la fase 3 y el daño de la fase 6, así que el último HP del lote es el correcto. No se descarta ni se consolida ningún evento.

## 6. Posiciones (`PositionUpdate`)

- **DECIDIDO (D-062, resuelve O-007):** se publica en los ticks **múltiplos de `N = TickRate / PositionRateHz`**. Con los valores aprobados, N = 2. La configuración se valida para que N sea entero. Es determinista: en 20 ticks hay exactamente 10 actualizaciones (prueba obligatoria de 09 §10).
- Contenido (RECOMENDADO D-070): `tick` y **todas las unidades vivas** al cierre del tick, como `{id, x, z}`. Va igual a ambos. Las coordenadas se redondean a `PositionDecimals` (3, milímetros): solo afecta a la vista, porque el servidor conserva las posiciones exactas.
- En un tick de posiciones, cada destinatario recibe primero `StateEvents` y después `PositionUpdate`. Una unidad creada en el tick aparece por `UnitSpawned` antes que su posición; una destruida en el tick ya no figura en las posiciones (09 §5-6).
- **Reglas para Godot (09 §5):** interpolar entre posiciones recibidas sin extrapolar; descartar las de un tick menor o igual al último aplicado; ignorar IDs desconocidos o ya destruidos. Una posición nunca crea ni revive una entidad.

## 7. Consolidación segura

**DECIDIDO (D-064):** `PositionUpdate` se encola como `DeliveryClass.Supersedable` y todo lo demás como `Essential`.

- **Semántica que debe implementar Networking:** si en la cola de una conexión hay un mensaje reemplazable del mismo tipo que **todavía no empezó a enviarse**, se quita, y el nuevo se agrega **al final**.
- **Por qué nunca cruza una barrera estructural (09 §7):** el mensaje nuevo nunca se adelanta a nada; solo desaparece uno viejo que ya quedó superado. Si entre los dos hubo un spawn o una destrucción, el nuevo va después de ese evento, igual que sin consolidar.
- Los `Essential` nunca se descartan ni se reordenan. Si no caben, Networking aplica su política de saturación (08 §5).
- No se toca un mensaje que ya empezó a enviarse (09 §7).

## 8. Orden por cliente

| Garantía (09 §6) | Cómo se cumple |
|---|---|
| `InitialState` antes de `ClientReady` | se encola en Starting, después de `PlayerAssigned` (contrato 07) |
| `MatchStarted` antes del gameplay | GameSession lo encola antes de `StartSimulation` (07 §7) |
| Ticks en orden creciente; eventos en orden oficial | un solo hilo publica (Simulation) y la proyección conserva el orden |
| Creación antes de posiciones; destrucción sin revivir | §6 y regla de Godot |
| `MapRevision` correcta | cada lote lleva `mapRevision`; `MapRevisionChanged` sigue al evento estructural (D-034) |
| Gameplay antes de `MatchEnded` | proyección sincrónica (D-064) + resultado con ticks detenidos (D-055) |
| Secuencia lógica por destinatario | el orden de `Enqueue` es el orden de la cola; no hay tareas paralelas que publiquen |

## 9. Convenciones JSON (`ProtocolJson.Options`)

**RECOMENDADO (D-072):**

- Propiedades en `camelCase`.
- Enums como string con el nombre del valor (`"Worker"`, `"Farm"`, `"Training"`); se rechazan los números.
- Las propiedades nulas se omiten.
- Eventos polimórficos con discriminador `"type"`, que siempre va primero.
- IDs como números. Las posiciones como `x`/`z` en metros.
- Los `ErrorCode` se publican como string (D-007).
- El sobre (`ProtocolVersion`, `Type`, `MessageId`) y el framing por línea los define Networking; los nombres de mensaje de State Sync son `InitialState`, `StateEvents` y `PositionUpdate`.

## 10. Configuración (`StateSyncConfig`)

| Parámetro | Regla | Valor |
|---|---|---|
| `PositionRateHz` | entre 1 y `TickRate`; `TickRate` múltiplo de él | **10** (DECIDIDO 09 §5) |
| `PositionDecimals` | 0 a 6 | 3 (RECOMENDADO) |

## 11. Lo que deben hacer los demás módulos

| Módulo | Obligación |
|---|---|
| **Simulation** | Llamar a `Publish` después de reemplazar `Current`, una vez por tick y en orden (06 §4); emitir `Owner` en `ProductionChanged` y `RepathRequested` (D-063); incluir en `Positions` todas las unidades vivas |
| **Networking** | Implementar `IClientOutbox` sin bloquear; resolver el destinatario con `ISessionGate.GetConnection` y descartar si está desconectado (D-069); implementar `Supersedable` como en §7; serializar con `ProtocolJson.Options` |
| **GameSession** | Pasar el `SessionId` en `ISessionEffects.PublishInitialState` |
| **Rts.Server** | Armar el `GameCatalog` desde las configuraciones (D-071); conectar `IStateSyncPublisher` como `ITickPublisher` de Simulation y como destino de `PublishInitialState`; validar `StateSyncConfig` con el `TickRate` |
| **Godot** | Copiar el esquema de `Rts.Protocol` (ejemplos de cada mensaje en `docs/protocol/examples/`); aplicar las reglas de posiciones (§6); tomar el balance del catálogo |

## 12. Pruebas previstas (09 §10)

- InitialState equivalente para ambos salvo datos privados, y con el catálogo.
- `ResourcesChanged`, `ProductionChanged` y `RepathRequested` solo al dueño; `CommandResult` solo al emisor.
- `BuildingPlaced` y `MapRevisionChanged` a ambos.
- `UnitSpawned` antes de la primera posición; unidad destruida ausente de las posiciones siguientes.
- 20 ticks ⇒ exactamente 10 `PositionUpdate`, y 30 Hz ⇒ cada 3 ticks.
- Lote ordenado por `index`; sin lote si no hay nada para el destinatario.
- Daño y progreso de una obra en el mismo tick: dos eventos, y el último HP es el correcto.
- Doble destrucción de bases: ambos reciben el lote final antes de `MatchEnded(Draw)` (con GameSession).
- Ida y vuelta JSON de cada DTO (ya cubierto en `Rts.Protocol.Tests` para las convenciones).
- Repetición de cientos de ticks sobre clientes simulados: convergencia del estado público y aislamiento de lo privado (RECOMENDADO 09 §10).

## 13. Pendientes

- ~~Política de saturación, sobre JSON, versión, `Hello`, sesión y comandos entrantes~~: definidos en el [contrato 08](08-networking.md). Quedan los valores de tamaños y capacidades.
- Optimización futura: publicar solo las posiciones que cambiaron. Requiere guardar estado por destinatario; hoy no se hace.
- Frecuencia de métricas de red.
