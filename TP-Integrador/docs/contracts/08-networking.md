# Contrato 08 — Networking

> **Estado:** primera versión de contratos implementada (`server/src/Rts.Networking`, DTOs en `server/src/Rts.Protocol`) y probada (`server/tests/Rts.Networking.Tests`, `server/tests/Rts.Protocol.Tests`). El servidor TCP, el framing, el parser y las colas **todavía no tienen lógica**.
> **Etiquetas:** **DECIDIDO** = aprobado por el equipo (ver [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Módulo:** [08 — Networking](../modules/08-networking.md). **Vecinos:** [06 Simulation](06-simulation.md), [07 GameSession](07-game-session.md), [09 State Sync](09-state-sync.md).

## 1. Superficie pública

| Tipo | Proyecto / namespace | Uso |
|---|---|---|
| `NetworkEnvelope`, `ProtocolMessageTypes`, `ProtocolJson` | `Rts.Protocol` | Sobre, lista cerrada de tipos y convenciones JSON |
| DTOs `Session/`, `Transport/`, `Commands/` | `Rts.Protocol` | Sesión, transporte y comandos de gameplay (los de estado están en el [contrato 09](09-state-sync.md)) |
| `NetworkingConfig` | `Rts.Networking.Configuration` | Todos los configurables aprobados (08 §10) |
| `ILineFrameDecoder`, `FrameReadResult` | `Rts.Networking.Framing` | Reconstrucción de líneas desde el flujo TCP |
| `IInboundMessageParser`, `ParseResult`, `InboundMessage` y derivados | `Rts.Networking.Parsing` | Validación estructural y conversión a tipos de dominio |
| `ICommandRateLimiter`, `IViolationTracker` | `Rts.Networking.Admission` | Límite por segundo y conteo de faltas, por conexión |
| `IOutboundQueue`, `EnqueueOutcome`, `ISessionMessageEncoder` | `Rts.Networking.Outbound` | Cola de salida por conexión y codificación de mensajes de sesión |
| `INetworkServer`, `ConnectionPhase`, `ConnectionMetricsSnapshot` | `Rts.Networking` | Servidor; implementa `IClientOutbox` |
| `NetworkErrors` | `Rts.Networking` | Códigos `Network.*` |

**Dependencias que usa** (inyectadas, todas de `Rts.Shared` o `Rts.Protocol`):

- `ISessionEventSink` e `ISessionGate`, implementados por GameSession;
- `ICommandSink`, implementado por Simulation (D-045);
- `TimeProvider`, para todos los plazos (reloj monotónico, reemplazable en pruebas).

Networking no referencia ningún módulo de dominio, ni Simulation, ni GameSession, ni State Sync.

## 2. Sobre y mensajes

**RECOMENDADO (D-076):** todo mensaje, en ambos sentidos, es una línea JSON UTF-8 terminada en `\n`, con este sobre:

```json
{"protocolVersion":1,"type":"Move","messageId":42,"payload":{ ... }}
```

| Campo | Regla |
|---|---|
| `protocolVersion` | Obligatorio en **cada** mensaje: 08 §2 pide validar la versión en la recepción. Debe ser igual a `NetworkingConfig.ProtocolVersion` |
| `type` | Uno de `ProtocolMessageTypes`; el cliente solo puede enviar los de `ClientToServer` |
| `messageId` | Obligatorio en gameplay: es el `ClientCommandId`, en [1, 2^53] (D-005). Opcional en el resto. El servidor no lo usa en sus mensajes |
| `payload` | El DTO del tipo, con las convenciones de `ProtocolJson` (D-072). Puede faltar en mensajes sin datos |

| Sentido | `type` | Payload | Entrega |
|---|---|---|---|
| C → S | `Hello` | `HelloDto(clientName?)` | — |
| C → S | `ClientReady` | `ClientReadyDto(initializationId)` | — |
| C → S | `Move`, `Stop`, `Attack`, `GatherWood`, `Build`, `RepairFarm`, `TrainUnit`, `RepathResponse` | `…CommandDto` | — |
| ambos | `Ping` / `Pong` | `PingDto(pingId)` / `PongDto(pingId)` | esencial |
| S → C | `PlayerAssigned`, `StartAborted`, `MatchStarted`, `MatchEnded`, `SessionRequestRejected` | DTOs de `Session/` | esencial |
| S → C | `InitialState`, `StateEvents` | contrato 09 | esencial |
| S → C | `PositionUpdate` | contrato 09 | **reemplazable** |
| S → C | `CommandRejected` | `CommandRejectedDto(messageId, error)` | esencial |
| S → C | `ProtocolError` | `ProtocolErrorDto(error, messageId?, detail?, serverProtocolVersion?)` | esencial |
| S → C | `Disconnecting` | `DisconnectingDto(reason)` | esencial; último mensaje antes de cerrar |

Los mensajes de entrada nunca traen `PlayerId`: la identidad sale de `ISessionGate.GetPlayer(conexión)` (08 §3). Un `PlayerId` falsificado no tiene dónde ir.

## 3. Ciclo de vida de una conexión

```
aceptada ──► AwaitingHello ──Hello válido──► HelloAccepted ──Close/error/EOF──► Closing ──► Closed
               │  (HandshakeTimeout)                                                          │
               └──────────────────────────── Disconnecting(HandshakeTimeout) ──────────────────┘
```

- **Aceptación:** si ya hay `MaxPendingConnections` conexiones en `AwaitingHello`, la nueva recibe `Disconnecting(Network.TooManyConnections)` y se cierra. No hay otro límite al aceptar: el tercer jugador lo rechaza GameSession después de su `Hello` (07 §2).
- **`AwaitingHello` (D-076):**
  - Solo se aceptan `Hello`, `Ping` y `Pong`. Cualquier otro mensaje recibe `ProtocolError(Network.HelloRequired)` y cuenta como falta.
  - Un `Hello` con otra versión recibe `ProtocolError(Network.UnsupportedVersion, serverProtocolVersion)` y después `Disconnecting(Network.UnsupportedVersion)` (08 §2).
  - Un `Hello` válido llama a `ISessionEventSink.TryPostHello` y pasa a `HelloAccepted` (D-053). Si la cola de sesión está llena, `Disconnecting(Network.SessionBusy)`.
  - Sin `Hello` dentro de `HandshakeTimeout`: `Disconnecting(Network.HandshakeTimeout)`.
- **`HelloAccepted`:** la plaza la decide GameSession, que responde `PlayerAssigned` o `Close(MatchFull)`. Un segundo `Hello` es una falta (`Network.DuplicateHello`).
- **Cierre:** toda conexión aceptada termina en `Closed` y se informa con `PostConnectionClosed` **exactamente una vez**, cualquiera sea la causa (EOF, error de socket, heartbeat, saturación, abuso, `Close` de GameSession, drenaje). GameSession ignora las conexiones sin plaza.

## 4. Recepción (`ReceiveLoop`, una por conexión)

1. Lee bytes de forma asincrónica y los pasa a su `ILineFrameDecoder` (08 §2):
   - delimitador `\n`, con un `\r` final tolerado;
   - las líneas vacías se ignoran;
   - una línea que supera `MaxInboundMessageBytes` es `Oversize` ⇒ `Disconnecting(Network.MessageTooLarge)` (D-074). Se detecta sin esperar el `\n`.
2. Cada línea completa pasa por `IInboundMessageParser.Parse`, que es puro y sin estado. Recorre UTF-8 → JSON → sobre → versión → tipo → `messageId` → payload → tipo de dominio.
   - Si falla con `ProtocolViolation` ⇒ `ProtocolError` y una falta.
   - Si es gameplay con `messageId` válido pero estructura inválida (por ejemplo, un `Build` que mezcla colocar y continuar, o una excepción de un constructor de comando) ⇒ `CommandRejected(Common.InvalidCommand)` y una falta.
3. Ruteo del mensaje válido:

| Mensaje | Acción |
|---|---|
| `Hello` | §3 |
| `ClientReady` | `ISessionEventSink.TryPostClientReady`; si devuelve `false`, `ProtocolError(Network.SessionBusy)`, sin falta |
| `Ping` | encola `Pong` con el mismo `pingId` |
| `Pong` | si coincide con el `Ping` pendiente, lo da por respondido; si no, se ignora (sin falta) |
| gameplay | §5 |

Cualquier mensaje válido cuenta como **entrada válida** y reinicia el tiempo de inactividad del heartbeat (08 §6).

El receptor no hace nada bloqueante: postear en la cola de sesión, admitir y encolar respuestas son operaciones no bloqueantes.

## 5. Admisión de gameplay (Command Admission)

Pasos en orden, para cada comando válido:

| # | Comprobación | Si falla | ¿Falta? |
|---|---|---|---|
| 1 | `ISessionGate.AcceptsGameplay` | `CommandRejected(Common.MatchNotRunning)` (08 §8, D-045) | no |
| 2 | `ISessionGate.GetPlayer(conexión)` | `CommandRejected(Network.NotRegistered)` | no |
| 3 | `ICommandRateLimiter.TryAcquire()` | `CommandRejected(Network.RateLimited)` (08 §4) | **sí** |
| 4 | `ICommandSink.Admit(player, clientCommandId, command)` | `CommandRejected(Common.CommandQueueFull)` | no |

- **DECIDIDO (D-075):** los rechazos antes de admitir usan `CommandRejected(messageId, error)`, distinto del `CommandResult` oficial. Como el comando no entró a Simulation, no queda en el registro de idempotencia y Godot puede **reintentarlo con el mismo `messageId`**.
- Un comando admitido no recibe respuesta inmediata. Su resultado oficial llega en el `StateEvents` del tick que lo procesa (contrato 09 §5), y un duplicado recibe el resultado original (D-043).
- `Admit` asigna `Sequence` y encola en una sola operación atómica (contrato 06 §7). Los dos receptores pueden llamarlo a la vez.
- **RECOMENDADO (D-077):** el límite por segundo es un **balde de fichas**: capacidad `CommandBurst`, recarga a `CommandsPerSecond`. Cuenta solo comandos de gameplay, `RepathResponse` incluido (D-028). Rechazar no quita nada ya admitido (08 §4).

## 6. Faltas y abuso

**DECIDIDO (D-074):**

- **Falta:**
  - cualquier mensaje inválido: UTF-8, JSON, sobre, versión, tipo, `messageId`, payload, `HelloRequired` o `DuplicateHello`;
  - un comando rechazado por el límite por segundo.
- **No son faltas:** `MatchNotRunning`, `NotRegistered`, `CommandQueueFull` y `SessionBusy`, porque no son culpa del cliente.
- Más de `MaxViolations` faltas dentro de `ViolationWindow` ⇒ `Disconnecting(Network.AbuseDetected)`.
- Un mensaje que supera el tamaño máximo cierra de inmediato, sin contar faltas.
- Un JSON malformado aislado **no** cierra la conexión (08 §2).

## 7. Salida (`IOutboundQueue` + `SendLoop`)

- **Una cola acotada y un escritor único por conexión (08 §5).**
  - Producen en ella: el hilo de Simulation (State Sync), el receptor (respuestas), el host de GameSession (sesión) y el monitor (heartbeat).
  - Solo el `SendLoop` escribe en el socket: arma el sobre, serializa con `ProtocolJson.Options`, agrega `\n` y escribe.
  - Ningún mensaje intercala sus bytes con otro.
- **Saturación (DECIDIDO D-073):**
  - La capacidad se mide en cantidad de mensajes (`OutboundQueueCapacity`).
  - Si un mensaje **esencial** no entra, `TryEnqueue` devuelve `Saturated` y se desconecta a ese cliente con `Network.OutboundSaturated`. Nunca se descarta un esencial para hacerle lugar, y la cola no crece sin límite.
  - Al cerrar por saturación se descartan los pendientes y se intenta enviar solo el `Disconnecting`, con `CloseLinger` como plazo.
- **Reemplazables (D-064):**
  - Si hay un `PositionUpdate` pendiente que el `SendLoop` todavía no tomó, se quita y el nuevo va al final (`EnqueueOutcome.Superseded`).
  - Como hay a lo sumo uno pendiente, las posiciones nunca saturan la cola.
- Un cliente lento solo llena su propia cola: no bloquea al otro cliente ni a Simulation (08 §5).
- **Cierre individual (`Close`):** encola `Disconnecting(reason)`, completa la cola, deja de leer y corta el socket al entregar o al vencer `CloseLinger`.
- **Drenaje final (`DrainAndCloseAllAsync`, 07 §9):**
  - deja de aceptar conexiones;
  - encola `Disconnecting(Network.ServerShuttingDown)` en cada conexión, detrás de `MatchEnded`;
  - espera que las colas se vacíen hasta `DrainTimeout` y cierra todo.

  Termina aunque haya sockets muertos.
- **RECOMENDADO (D-078):** la cola se implementa con una lista enlazada protegida por un lock, más una señal para el consumidor, porque `Channel` no permite quitar un reemplazable intermedio. El lock solo protege la lista y nunca se toma junto con otro lock.

## 8. Heartbeat

**RECOMENDADO (D-079)**, sobre lo DECIDIDO en 08 §6:

- Si pasa `PingIdleInterval` sin **entrada válida** del cliente, el servidor envía `Ping` con un `pingId` correlativo por conexión.
- Si no llega el `Pong` con ese `pingId` dentro de `PongTimeout`, la conexión se da por perdida: `Disconnecting(Network.HeartbeatTimeout)` y `PostConnectionClosed`. GameSession decide el efecto (victoria del otro en Running).
- Enviar mensajes al cliente no cuenta como actividad (08 §6). Cualquier mensaje válido del cliente sí, incluido un `Pong`.
- El heartbeat es simétrico: Godot puede enviar `Ping` para detectar un servidor caído, y el servidor responde `Pong` con el mismo `pingId`.
- Un solo monitor periódico revisa handshake y heartbeat de todas las conexiones. Usa `TimeProvider` y no depende de los ticks, así que funciona en todas las fases de la sesión (08 §6).
- Peor caso de detección: `PingIdleInterval + PongTimeout`. Debe ser prudente, porque en Running una desconexión hace ganar al otro.

## 9. Concurrencia

| Componente | Hilo | Toca |
|---|---|---|
| Bucle de aceptación | 1 tarea | registro de conexiones |
| `ReceiveLoop` | 1 tarea por conexión | su decodificador, limitador y contador de faltas (no compartidos); `ICommandSink`, `ISessionEventSink`, su cola de salida |
| `SendLoop` | 1 tarea por conexión | su cola (único consumidor) y su socket (único escritor) |
| Monitor | 1 tarea periódica | marcas de tiempo de cada conexión; colas de salida |
| `IClientOutbox.Enqueue` | hilo de Simulation | `ISessionGate.GetConnection` + cola de salida |

- Networking nunca escribe `WorldState` ni `GameSessionState`. Lee la sesión solo a través de `ISessionGate`, que respalda una instantánea inmutable.
- Ninguna operación llamada desde el tick bloquea: `Enqueue` toma un lock breve de la cola y vuelve.
- No hay locks cruzados entre módulos (AGENTS §6).

## 10. Configuración (`NetworkingConfig`)

| Parámetro | Regla | Valor |
|---|---|---|
| `ListenAddress`, `Port` | IP válida; 1–65535 | PENDIENTE |
| `ProtocolVersion` | > 0 | 1 |
| `MaxInboundMessageBytes` | > 0 | PENDIENTE |
| `MaxPendingConnections` | > 0 | PENDIENTE |
| `HandshakeTimeout` | > 0 | PENDIENTE |
| `CommandsPerSecond`, `CommandBurst` | > 0; ráfaga ≥ por segundo | PENDIENTE |
| `OutboundQueueCapacity` | ≥ 2 | PENDIENTE |
| `MaxViolations`, `ViolationWindow` | > 0 | PENDIENTE |
| `PingIdleInterval`, `PongTimeout` | > 0 | PENDIENTE |
| `CloseLinger`, `DrainTimeout` | > 0 | PENDIENTE |

## 11. Lo que deben hacer los demás

| Quién | Obligación |
|---|---|
| **Rts.Server** | Implementar `ISessionEffects` de GameSession sobre `INetworkServer`: `Send` → `Send`, `Close` → `Close`, `DrainAndCloseAllAsync` → `DrainAndCloseAllAsync`, y `PublishInitialState` → State Sync. Inyectar `INetworkServer` como `IClientOutbox` de State Sync. Validar `NetworkingConfig` al iniciar |
| **GameSession** | Respaldar `ISessionGate` con una instantánea inmutable (contrato 07 §8) |
| **Simulation** | Implementar `ICommandSink.Admit` como operación atómica, segura para varios hilos y no bloqueante (contrato 06 §7) |
| **Godot** | Programar contra los ejemplos de `docs/protocol/examples/` (generados desde estos DTOs); enviar `Hello` primero; incluir `protocolVersion` en cada mensaje; usar `messageId` único y creciente por comando; tratar `CommandRejected` como reintentable y `CommandResult` como definitivo; responder `Ping` con `Pong`; terminar cada mensaje con `\n` |

## 12. Pruebas previstas (08 §9)

- **Framing:** un mensaje en varias lecturas; varias líneas en una lectura; `\r\n`; línea vacía; `Oversize` antes del `\n`.
- **Parser:** UTF-8 inválido; JSON malformado; sobre sin versión o tipo; versión distinta; tipo desconocido o de salida; gameplay sin `messageId` o fuera de rango; `Build` con formas mezcladas. En todos los casos, sin ejecución y con la conexión abierta.
- **Handshake:** `Hello` con versión incompatible ⇒ error y cierre; mensaje antes de `Hello`; timeout de handshake; tope de conexiones pendientes.
- **Admisión concurrente:** dos clientes con 500 comandos cada uno ⇒ secuencias únicas y en orden; un duplicado llega a Simulation y se responde con el resultado original; flood ⇒ `RateLimited` sin quitar lo admitido; abuso ⇒ cierre.
- **Identidad:** un `playerId` dentro de un payload se ignora.
- **Salida:**
  - un escritor por conexión, sin mensajes mezclados;
  - un cliente lento no bloquea al otro ni a los ticks;
  - con la cola llena, un esencial provoca el cierre;
  - un `PositionUpdate` pendiente se reemplaza sin adelantarse a eventos.
- **Heartbeat:** `Ping` tras inactividad; un `Pong` válido mantiene la conexión; timeout ⇒ `PostConnectionClosed` una sola vez.
- **Cierre:** `MatchEnded` y después `Disconnecting(ServerShuttingDown)`; el drenaje termina con un socket muerto.

## 13. Pendientes

- Valores de todos los configurables (§10).
- Pruebas de ida y vuelta contra el cliente Godot real (08 §11).
- Proyecto `Rts.Server`: composición de todos los módulos y programa principal.
