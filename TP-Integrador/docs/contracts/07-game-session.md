# Contrato 07 — GameSession

> **Estado:** primera versión de contratos implementada (`server/src/Rts.GameSession`, tipos de integración en `Rts.Shared.Session`) y probada (`server/tests/Rts.GameSession.Tests`). La máquina de estados y el host **todavía no tienen lógica**.
> **Etiquetas:** **DECIDIDO** = aprobado por el equipo (ver [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Módulo:** [07 — GameSession](../modules/07-game-session.md). **Vecinos:** [06 Simulation](06-simulation.md), [08 Networking](../modules/08-networking.md), [09 State Sync](../modules/09-state-sync.md).

## 1. Superficie pública

**RECOMENDADO (D-059):** GameSession es una **máquina de estados pura** más un **host** que serializa eventos y ejecuta acciones. Los tipos que necesitan Networking y State Sync viven en `Rts.Shared.Session`, así que ninguno de ellos referencia el proyecto GameSession (mismo criterio que D-049).

| Tipo | Proyecto / namespace | Uso |
|---|---|---|
| `SessionStatus` | `Rts.Shared.Session` | Los cinco estados DECIDIDOS (07 §3) |
| `ISessionEventSink` | `Rts.Shared.Session` | Networking → GameSession: `TryPostHello`, `TryPostClientReady`, `PostConnectionClosed` |
| `ISessionGate` | `Rts.Shared.Session` | Networking lee `Status`, `AcceptsGameplay`, `GetPlayer(ConnectionId)` y `GetConnection(PlayerId)` desde sus hilos (la última, para dirigir publicaciones de State Sync, D-069) |
| `ISessionMessage` y mensajes | `Rts.Shared.Session` | `PlayerAssigned`, `StartAborted`, `MatchStarted`, `MatchEnded`, `SessionRequestRejected` (modelos, no DTOs JSON) |
| `IMatchResultSink`, `ISimulationControl`, `MatchResult` | `Rts.Shared.Match` | Simulation ↔ GameSession (contrato 06) |
| `GameSessionState`, `PlayerSession` | `Rts.GameSession.Model` | Estado inmutable; GameSession es su único escritor |
| `SessionEvent` y derivados | `Rts.GameSession.Events` | Entradas ya serializadas de la máquina de estados |
| `SessionAction` y derivadas | `Rts.GameSession.Actions` | Efectos propuestos, en orden de ejecución |
| `ISessionStateMachine`, `SessionTransition` | `Rts.GameSession` | `Apply(state, event) → (estado nuevo, acciones)` |
| `ISessionInbox` | `Rts.GameSession` | Buzón: implementa `ISessionEventSink` (Networking) e `IMatchResultSink` (Simulation); pasivo, sin dependencias (D-082) |
| `ISessionStateStore` | `Rts.GameSession` | Vista: implementa `ISessionGate`; la última instantánea confirmada; pasiva, sin dependencias (D-082) |
| `ISessionHost` | `Rts.GameSession` | Consumidor único: lee el buzón, aplica la máquina de estados, publica en la vista y ejecuta las acciones |
| `ISessionEffects` | `Rts.GameSession` | Puerto hacia Networking y State Sync; lo implementa `Rts.Server` |
| `GameSessionConfig`, `SessionErrors` | `Rts.GameSession` | Configuración y códigos `Session.*` |

## 2. Estado (`GameSessionState`)

Inmutable: cada transición crea uno nuevo. Campos: `Session`, `Status`, `Player1`, `Player2` (`PlayerSession?`), `CurrentInitialization`, `LastInitialization`, `StopRequested`, `PendingDisconnects`, `ShutdownRequested`, `Result`.

`PlayerSession(Player, Connection, IsConnected, IsReady)` solo guarda identificadores (07 §8). Un desconectado no puede estar listo.

**Invariantes que verifica el constructor:**

| Estado | Plazas | Inicialización | Otros |
|---|---|---|---|
| `WaitingForPlayers` | a lo sumo una, conectada y sin Ready | sin vigente | sin resultado, sin detención |
| `Starting` | ambas conectadas | vigente = última emitida | sin resultado, sin detención |
| `Running` | ambas; las conectadas, listas | la que arrancó la partida | desconectados = `PendingDisconnects`; `StopRequested` si y solo si hay desconexiones pendientes o `ShutdownRequested` |
| `Ending`, `Ended` | cualquiera | — | resultado fijado; sin detención pendiente |

"WaitingForPlayers con ambas plazas ocupadas" es imposible por construcción: es el estado trabado que 07 §4 pide evitar.

`AcceptsGameplay` = `Running` y sin `StopRequested`. `NextInitialization` = `LastInitialization.Next()`, o `First`. Nunca se reutiliza un `InitializationId` (07 §4).

## 3. Tabla de transiciones (`ISessionStateMachine.Apply`)

`Apply` es una función pura: sin I/O, sin reloj, sin hilos. Para el mismo estado y el mismo evento devuelve la misma transición. Nunca lanza por eventos tardíos, duplicados o de conexiones desconocidas: los ignora o los rechaza con un mensaje.

"Registrada" = la conexión ocupa una plaza en el estado actual. Las acciones se ejecutan **en el orden listado**.

| Estado | Evento | Condición | Estado nuevo | Acciones |
|---|---|---|---|---|
| cualquiera | `ClientHelloReceived` | registrada | igual | `SessionRequestRejected(Session.AlreadyRegistered)` |
| `WaitingForPlayers` | `ClientHelloReceived` | queda una sola plaza ocupada | `WaitingForPlayers` | `PlayerAssigned` |
| `WaitingForPlayers` | `ClientHelloReceived` | completa las dos plazas | `Starting`, con `init = NextInitialization` | `PlayerAssigned` al nuevo → `PrepareInitialState(init, P1, P2)` → `ScheduleReadyTimeout(init, ReadyTimeout)` |
| `Starting`, `Running`, `Ending`, `Ended` | `ClientHelloReceived` | no registrada | igual | `CloseConnection(Session.MatchFull)` |
| `Starting` | `ClientReadyReceived` | `init` vigente, todavía no listo | marca listo; si ambos están listos → `Running` | si ambos: `MatchStarted` a P1 → `MatchStarted` a P2 → `StartSimulation` |
| `Starting` | `ClientReadyReceived` | `init` vigente, ya listo | igual | ninguna (duplicado idempotente: no inicia dos veces) |
| `Starting` | `ClientReadyReceived` | `init` distinto | igual | `SessionRequestRejected(Session.StaleInitialization)` |
| otro | `ClientReadyReceived` | registrada | igual | `SessionRequestRejected(Session.NotStarting)` |
| cualquiera | `ClientReadyReceived` | no registrada | igual | `SessionRequestRejected(Session.NotRegistered)` |
| `WaitingForPlayers` | `ConnectionClosed` | registrada | libera la plaza | ninguna |
| `Starting` | `ConnectionClosed` | registrada | `WaitingForPlayers`: libera la plaza, el otro queda sin Ready, sin inicialización vigente | `StartAborted(init, OpponentLeft)` al otro |
| `Running` sin detención | `ConnectionClosed` | registrada | `StopRequested`; el jugador pasa a desconectado y entra al lote | `StopSimulation` |
| `Running` con detención | `ConnectionClosed` | registrada y conectada | el jugador entra al lote | ninguna |
| `Ending`, `Ended` | `ConnectionClosed` | registrada | marca desconectado; el resultado no cambia | ninguna |
| cualquiera | `ConnectionClosed` | no registrada | igual | ninguna (terceros, expulsados, clientes sin Hello) |
| `Starting` | `ReadyTimeoutElapsed` | `init` vigente | `WaitingForPlayers`: libera las plazas no listas; las listas quedan sin Ready | `CloseConnection(Session.ReadyTimeoutExpired)` a cada no listo → `StartAborted(init, ReadyTimeout)` a cada listo |
| otro | `ReadyTimeoutElapsed` | — | igual | ninguna (timer de un intento ya cancelado o iniciado) |
| `Starting` | `InitializationFailed` | — | `Ending` con `CriticalError(null)` | `MatchEnded` a cada conectado → `ShutdownConnections` |
| `Running` | `SimulationResultReceived` | — | `Ending` con ese resultado; vacía el lote | `StopSimulation` (si no se había pedido) → `MatchEnded` a cada conectado → `ShutdownConnections` |
| `Running` con detención | `SimulationStopped(t)` | — | `Ending`: un desconectado ⇒ `PlayerDisconnected(otro, t)`; dos ⇒ `BothPlayersDisconnected(t)`; ninguno (solo apagado) ⇒ `ServerShutdown(t)` | `MatchEnded` a cada conectado → `ShutdownConnections` |
| `WaitingForPlayers`, `Starting` | `ShutdownRequested` | — | `Ending` con `ServerShutdown(null)` | `ShutdownConnections` (no hubo partida: el motivo llega en `Disconnecting(ServerShuttingDown)`) |
| `Running` sin detención | `ShutdownRequested` | — | `ShutdownRequested` y `StopRequested` | `StopSimulation` |
| `Running` con detención | `ShutdownRequested` | — | `ShutdownRequested` | ninguna (ya se pidió detener) |
| `Ending` | `ShutdownCompleted` | — | `Ended` | `CompleteSession(result)` |
| otro | `SimulationResultReceived`, `SimulationStopped`, `InitializationFailed`, `ShutdownCompleted`, `ShutdownRequested` | — | igual | ninguna (en `Ending` y `Ended` el resultado ya está fijado) |

**Apagado del servidor (DECIDIDO D-084):** cancelar `RunAsync` (Ctrl+C o SIGTERM) se convierte en `ShutdownRequested`. Solo fija resultado si no hay otro: un resultado de Simulation del último tick prevalece, y las desconexiones del lote siguen la regla de D-055. Si no hay ninguno de los dos, el resultado es `TechnicalEnd` con motivo `ServerShutdown`. En Running, como cualquier fin, primero se detienen los ticks, así `MatchEnded` sigue quedando detrás del último lote.

## 4. Plazas y handshake

- **DECIDIDO (D-053, resuelve O-005):** una conexión ocupa plaza **recién después de un `Hello` con versión compatible**. Networking valida la versión y solo entonces llama a `TryPostHello`. Un cliente con versión incompatible, o que nunca manda `Hello`, nunca ocupa plaza: lo corta Networking con su propio timeout de handshake (PENDIENTE en el contrato de Networking).
- **DECIDIDO (D-054, resuelve O-008):** el cliente nuevo ocupa **la plaza libre**. Si P1 se fue antes de Running y P2 sigue, el nuevo es P1 y el que quedó sigue siendo P2. El `PlayerId` de una conexión no cambia mientras vive. "Primero = P1, segundo = P2" (07 §2) describe el llenado inicial, con ambas plazas libres.
- Un tercer cliente, o cualquiera que llegue fuera de `WaitingForPlayers`, recibe `CloseConnection(Session.MatchFull)` (07 §2).
- **RECOMENDADO (D-059):** el rechazo no es un mensaje de sesión aparte. `CloseConnection` lleva el motivo, y Networking envía un aviso final con ese código antes de cerrar: el mensaje `Disconnecting(reason)` (D-080, [contrato 08 §3](08-networking.md#3-ciclo-de-vida-de-una-conexión)).

## 5. Inicio y `ReadyTimeout`

- Al completarse las dos plazas: `PlayerAssigned` al nuevo → `PrepareInitialState` → `ScheduleReadyTimeout`. El host ejecuta `PrepareInitialState` así: llama a `ISimulationControl.InitializeMatch()` y pasa el `WorldView` a `ISessionEffects.PublishInitialState`, que lo entrega a State Sync. Para cada cliente, `PlayerAssigned` queda antes que `InitialState`.
- **DECIDIDO (D-056, resuelve O-010 parte a):** al vencer `ReadyTimeout`, se expulsa a quien no confirmó y se libera su plaza. Quien sí confirmó se queda, con su Ready reiniciado, y la sesión vuelve a `WaitingForPlayers`. Si ninguno confirmó, se expulsa a ambos y la sesión queda vacía. Nunca queda un estado trabado.
- **DECIDIDO (D-057):** el jugador que se queda recibe `StartAborted(InitializationId, Reason)`, con `Reason = OpponentLeft` o `ReadyTimeout`. Godot descarta el mundo cargado y vuelve a esperar. Es un mensaje de sesión que se agrega a los de 07 §9.
- Al salir de `Starting`, la inicialización deja de ser vigente. Un Ready viejo recibe `Session.StaleInitialization` (07 §4), y su timer pendiente se ignora porque compara `InitializationId`.
- **RECOMENDADO (D-060):** si `InitializeMatch` o la publicación del InitialState fallan, el host informa `InitializationFailed` y la sesión termina con `CriticalError` sin `FinalTick`. Reintentar no sirve: con el mismo mapa y la misma configuración, falla igual.

## 6. Desconexiones en Running: lote y precedencia

**DECIDIDO (D-055, resuelve O-010 parte b y el PENDIENTE de 07 §6):** el resultado se resuelve **después de detener los ticks**.

1. Ante la primera desconexión en Running, GameSession **no fija resultado**: marca `StopRequested`, deja de aceptar gameplay (`AcceptsGameplay = false`) y pide `StopSimulation`.
2. El **lote** son todas las desconexiones que llegan hasta que el tick en curso termina (`SimulationStopped`). Dura como mucho un tick, unos 50 ms.
3. Si en ese intervalo llega un resultado de Simulation (victoria, empate o falla crítica del último tick), **ese resultado prevalece**, porque es un hecho del mundo ya confirmado.
4. Si no, al recibir `SimulationStopped(t)`: una desconexión ⇒ gana el otro (`PlayerDisconnected`); dos ⇒ final técnico (`BothPlayersDisconnected`). `FinalTick = t`.
5. Con el resultado fijado (`Ending`), las desconexiones posteriores no lo modifican (07 §5).

```
 Simulation  ──tick N──┤commit N├──(publica N)──[sink: resultado?]──stop──┐
 GameSession ──Closed(P2)→ StopRequested ──···lote···──────────────────────→ SimulationStopped(N)
                                                                              ⇒ resultado de N, si existe;
                                                                                si no, desconexiones del lote
```

**Garantía que esta regla exige de Simulation (contrato 06 §6):** el loop publica el tick y llama a `IMatchResultSink.OnSimulationResult` **antes** de que termine `StopTicksAsync`. Así, en la cola de sesión el resultado de Simulation siempre llega antes que `SimulationStopped`, y la precedencia no depende de una carrera entre hilos.

**Invariante de cierre:** el resultado se fija solo cuando ya no puede correr ningún tick. Por eso `MatchEnded` puede encolarse en el mismo momento y queda **después** del último lote de gameplay (07 §9, 09 §5).

## 7. Orden de mensajes por cliente

Para cada conexión, el orden de las acciones es el orden de la cola de salida (08 §5):

`PlayerAssigned` → `InitialState` (State Sync) → [`StartAborted` → nuevo `InitialState` …] → `MatchStarted` → publicaciones de gameplay → `MatchEnded` → aviso de cierre.

- `MatchStarted` se encola para ambos **antes** de `StartSimulation`, así ninguna publicación de gameplay puede adelantarse.
- `MatchEnded` se encola cuando los ticks ya están detenidos (§6).

## 8. Host (`ISessionHost`)

- **Tres piezas (DECIDIDO D-082):**
  - el **buzón** (`ISessionInbox`) recibe los eventos desde cualquier hilo;
  - la **vista** (`ISessionStateStore`) guarda la última instantánea y respalda `ISessionGate`;
  - el **consumidor** (`ISessionHost`) lee el buzón de a un evento, aplica `Apply`, publica el estado nuevo en la vista (una referencia inmutable) y ejecuta las acciones.

  El buzón y la vista no dependen de nada, así que se crean antes que Networking y Simulation, y la composición queda lineal (contrato 10 §4).
- **Cola (RECOMENDADO D-059):** está acotada por `EventQueueCapacity` solo para `Hello` y `ClientReady`; `TryPost*` devuelve `false` si está llena. `PostConnectionClosed` y los avisos internos (timer, Simulation, detención, drenaje) **nunca se descartan**: hay a lo sumo uno por conexión o por intento, así que su cantidad ya está acotada.
- **Acciones asincrónicas:** `StopSimulation` y `ShutdownConnections` corren fuera del consumidor y vuelven como eventos (`SimulationStopped`, `ShutdownCompleted`). Si `StopTicksAsync` falla, el host informa `SimulationStopped(null)` igual.
- **Timer:** `ScheduleReadyTimeout` usa `TimeProvider`, que es monotónico y reemplazable en pruebas, nunca ticks (07 §4).
- **Fin (DECIDIDO D-058):** al llegar a `Ended`, `RunAsync` devuelve el resultado y el proceso termina, con código 0, o distinto de 0 si el motivo fue `CriticalError`. Para otra partida se lanza el servidor de nuevo.
- No toma locks sobre `WorldState` ni llama a módulos de dominio (07 §7).

## 9. Configuración (`GameSessionConfig`)

| Parámetro | Regla | Valor |
|---|---|---|
| `ReadyTimeout` | > 0, reloj monotónico | PENDIENTE |
| `EventQueueCapacity` | > 0; solo limita Hello y Ready | PENDIENTE |

La espera del segundo jugador no tiene parámetro, porque es ilimitada (DECIDIDO). El plazo de drenaje al cerrar es de Networking (07 §9).

## 10. Lo que deben hacer los demás módulos

| Módulo | Obligación |
|---|---|
| **Networking** | Validar la versión antes de `TryPostHello` (D-053); informar `PostConnectionClosed` exactamente una vez por conexión; tomar el `PlayerId` de cada comando **solo** de `ISessionGate.GetPlayer`; rechazar gameplay con `Common.MatchNotRunning` si `AcceptsGameplay` es falso (D-045); en `Close`, enviar el aviso final con el motivo; en `DrainAndCloseAllAsync`, respetar su plazo de drenaje |
| **State Sync** | `PublishInitialState` arma un InitialState por destinatario con el `SessionId` y el `InitializationId` recibidos. El orden de `MatchEnded` detrás del último lote lo garantiza la proyección sincrónica (D-064, [contrato 09 §2](09-state-sync.md#2-flujo-de-publicación)) |
| **Simulation** | Llamar al sink **antes** de completar `StopTicksAsync` (§6); `StopTicksAsync` devuelve el último tick confirmado; `InitializeMatch` puede llamarse en cada nueva preparación |
| **Rts.Server** | Implementar `ISessionEffects` conectando Networking y State Sync; pasar el buzón como `IMatchResultSink` de Simulation y como `ISessionEventSink` de Networking, y la vista como `ISessionGate`; terminar el proceso con el código de `RunAsync` (contrato 10) |

## 11. Pruebas previstas (07 §10)

Con la máquina de estados, sin sockets, mediante secuencias de eventos:

- P1/P2 por conexión, tercero cerrado con `MatchFull`, espera ilimitada con un jugador.
- Sin `StartSimulation` hasta tener los dos Ready; Ready duplicado o viejo no inicia.
- Desconexión en Starting: libera plaza, `StartAborted` al otro, el nuevo recibe la plaza libre (D-054) y un `InitializationId` nuevo.
- ReadyTimeout con uno listo, con ninguno listo, y timer de un intento ya cancelado.
- Desconexión de P1 en Running ⇒ gana P2 después de `SimulationStopped`; dos en el mismo lote ⇒ final técnico; resultado de Simulation dentro del lote ⇒ prevalece.
- Resultado fijado no cambia; eventos después de `Ended` no tienen efecto.
- Con el host: eventos concurrentes desde varios hilos no corrompen el estado; orden `MatchStarted` → `StartSimulation` y `MatchEnded` después de detener.

## 12. Pendientes

- Valores de `ReadyTimeout` y `EventQueueCapacity` (balance y entorno).
- ~~DTOs JSON de sesión y aviso de cierre; timeout de handshake y drenaje~~: definidos en el [contrato 08](08-networking.md). Quedan sus valores numéricos.
