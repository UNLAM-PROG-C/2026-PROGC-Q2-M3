# 07 — GameSession

> **Estado:** ciclo de vida de partida aprobado; detalles de timeout y modelos concretos pendientes.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Simulation](06-simulation.md), [Networking](08-networking.md), [State Sync](09-state-sync.md).

## 1. Propósito — DECIDIDO

Administrar **una sesión de partida RTS 1v1** en un proceso servidor dedicado: registro de exactamente dos jugadores humanos, asignación de plazas, preparación/inicio sincronizado, supervisión de desconexiones, recepción de resultados y finalización. GameSession es **único escritor de `GameSessionState`**, pero **no** del `WorldState`; este último pertenece a Simulation.

No implementa matchmaking, lobby, espectadores, reconexiones, IA, múltiples partidas simultáneas por proceso ni base de datos.

## 2. Jugadores, registro y espera — DECIDIDO

- Primer cliente aceptado y registrado: **PlayerId 1**; segundo: **PlayerId 2**.
- Cualquier **tercera conexión** se rechaza con razón `MatchFull`/partida completa. Networking transporta la respuesta y cierra esa conexión.
- Con un solo jugador conectado, se espera **indefinidamente** a que aparezca el otro: **no existe timeout para la espera del segundo jugador**.
- Si alguien se desconecta **antes de Running**, se libera su plaza y se vuelve a esperar otro jugador. No se concede victoria por desconexión prepartida.
- Reconexión a una partida ya iniciada: **no disponible**.
- Ningún cliente elige de forma confiable su PlayerId; se asocia a la conexión que GameSession registró.

## 3. Máquina de estados — DECIDIDO

| Estado | Significado |
|---|---|
| `WaitingForPlayers` | Se espera completar dos clientes |
| `Starting` | Ambos registrados; inicialización y snapshot enviados; esperando `Ready` válidos |
| `Running` | Ambos listos; Simulation ejecuta ticks/acepta gameplay |
| `Ending` | Se ha fijado resultado o cierre técnico; se coordinan avisos/detención |
| `Ended` | Se finalizaron recursos y no se aceptan más comandos |

### Transiciones normales

1. Servidor inicia `WaitingForPlayers`.
2. Con ambos jugadores, pasa a `Starting`.
3. GameSession solicita a Simulation inicializar `WorldState`, y State Sync prepara un `InitialState` por cliente.
4. Cada Godot carga su estado inicial y envía `ClientReady` con `InitializationId` correspondiente.
5. **Solo cuando ambos Ready válidos están confirmados** se comunica `MatchStarted` y comienza el loop de Simulation → `Running`.
6. Simulation entrega resultado por victoria/empate o GameSession resuelve desconexiones → `Ending`.
7. Se detienen entradas/ticks y se envía `MatchEnded` de mejor esfuerzo con el orden correcto antes de `Ended`.

**No hay ticks de gameplay durante WaitingForPlayers o Starting**; mensajes de juego prematuros se rechazan. Un `Ready` viejo o duplicado no debe iniciar dos veces.

## 4. Timeout de Ready — DECIDIDO / PENDIENTE

- **DECIDIDO:** `ReadyTimeout` configurable; si ambos ya están conectados pero alguno no confirma dentro del plazo, se **aborta ese intento de inicio** y se vuelve al proceso de espera/preparación, sin comenzar ticks.
- **DECIDIDO:** no confundirlo con espera ilimitada por el **segundo jugador**; son etapas diferentes.
- **DECIDIDO:** al abandonar `Starting`, invalidar `InitializationId` y los `Ready` anteriores; cualquier futura preparación usa nueva inicialización.
- **PENDIENTE:** política exacta sobre **qué conexión se conserva o se expulsa al vencer ReadyTimeout** cuando ambos sockets siguen abiertos. No basta transicionar nominalmente a WaitingForPlayers con ambas plazas ocupadas: evitar estado imposible de desbloquear.
- **RECOMENDADO:** expulsar al cliente que no confirmó, conservar al que sí confirmó con Ready reiniciado, liberar plaza y volver a WaitingForPlayers; si ninguno confirmó, definir tratamiento de ambos. Esta es una sugerencia aún no aprobada, no un requisito.

> **Nota (2026-11-10):** la política al vencer `ReadyTimeout` quedó **DECIDIDA** en [D-056](../decisions.md): se adopta la recomendación anterior y, si ninguno confirmó, se expulsa a ambos. El jugador que queda recibe `StartAborted` (D-057). Ver [contrato 07 §5](../contracts/07-game-session.md#5-inicio-y-readytimeout).
- Los plazos de espera prepartida usan un reloj monotónico, **no** ticks de Simulation.

## 5. Desconexiones y resultados — DECIDIDO

### Durante Running

- **Un solo jugador se desconecta:** GameSession declara **victoria del jugador que permanece conectado**; motivo `PlayerDisconnected`.
- **Ambos jugadores se desconectan antes de que exista un resultado oficial:** finalización **técnica sin ganador** cuando las dos desconexiones pertenecen al mismo conjunto/lote de eventos de sesión antes de fijar resultado.
- Si un resultado de victoria/empate ya fue **oficialmente confirmado**, desconexiones posteriores **no lo modifican**.
- Si la finalización fue un error interno crítico de Simulation, resultado técnico, no victoria por destrucción de base.
- `MatchEnded` se intenta enviar al cliente que siga conectado. No hay garantía de entrega a quien perdió el socket.

### Resultados de gameplay entregados por Simulation

- Solo Centro Principal P1 destruido → ganador P2, `BaseDestroyed`.
- Solo Centro Principal P2 destruido → ganador P1, `BaseDestroyed`.
- **Ambos destruidos en un mismo tick** → `Draw`, sin ganador.
- Todo resultado de gameplay es calculado por Simulation, no por GameSession.

## 6. Confirmación de finalización única — DECIDIDO

- El resultado definitivo **solo puede establecerse una vez**; GameSession serializa sus eventos y transiciones.
- Una desconexión, una finalización de gameplay y un cierre técnico pueden llegar casi simultáneamente. Debe prevalecer el **primer resultado oficialmente confirmado** según el orden/agrupación de eventos definido, sin sobrescrituras.
- Para dos desconexiones observadas **dentro de un mismo lote de sesión antes de fijar resultado**, aplicar finalización técnica sin ganador, no declarar arbitrariamente ganador al segundo que se procesó.
- `Ending` impide que se acepten nuevos comandos/ticks después de comunicar la parada. `Ended` es terminal para ese proceso de partida.
- **PENDIENTE:** contrato temporal exacto para conciliar resultados de Simulation y eventos de Networking recibidos durante el cierre de tick; no inventar precedencia distinta de las decisiones anteriores.

> **Nota (2026-11-10):** resuelto por [D-055](../decisions.md): las desconexiones en Running se resuelven después de detener los ticks; el lote son las desconexiones observadas hasta que termina el tick en curso, y un resultado de Simulation de ese último tick prevalece. Ver [contrato 07 §6](../contracts/07-game-session.md#6-desconexiones-en-running-lote-y-precedencia).

## 7. Concurrencia — DECIDIDO

- Networking puede generar eventos desde **dos lectores TCP concurrentes**, además de timers y avisos de Simulation.
- Los eventos de sesión (`Connected`, `ClientReady`, `Disconnected`, `SimulationMatchResult`, `ReadyTimeout`) se **serializan** antes de mutar `GameSessionState`.
- **Un solo consumidor/escritor** maneja el estado de sesión, evitando asignar P1/P2 doblemente o terminar dos veces.
- GameSession no toma locks sobre entidades de WorldState ni llama a Combat/Economy.
- Heartbeat y supervisión de sockets son independientes de los ticks de gameplay.

## 8. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| Contrato/operación | Función |
|---|---|
| `GameSessionState` | `SessionId`, Status, Player1/Player2, InitializationId, ReadyDeadline, MatchResult, EndReason |
| `PlayerSession` | PlayerId, ConnectionId, IsConnected, IsReady |
| `MatchResult` | ResultType, WinnerPlayerId opcional, EndReason, FinalTickId opcional |
| `RegisterPlayer` | Admisión/rechazo de conexión y asignación de plaza |
| `HandleClientReady` | Validar Ready, InitializationId y conexión registrada |
| `TryStartMatch` | Comprobar ambos listos y producir acción de inicio |
| `HandlePlayerDisconnected` | Determinar efectos según estado |
| `HandleReadyTimeout` | Abort/start rollback de intento de inicio |
| `HandleSimulationResult` | Establecer resultado recibido de Simulation |
| `TerminateSession` | Finalización única y acciones de publicación/cierre |

Los nombres son propuestos; no constituyen firmas definitivas. `PlayerSession` **no contiene objetos de socket**. GameSession devuelve acciones propuestas a los coordinadores de red y Simulation, no I/O directo.

## 9. Mensajes y coordinación externa — DECIDIDO

- `PlayerAssigned`: informar plaza al cliente.
- `InitialState`: enviado por State Sync con `InitializationId`.
- `ClientReady`: respuesta de Godot con inicialización correcta.
- `MatchStarted`: después de ambos Ready válidos y **antes** de publicaciones gameplay.
- `MatchEnded`: resultado final de juego/desconexión/error, **después** de los últimos eventos gameplay relevantes.
- Networking acepta/rechaza sockets y detecta cierres; GameSession decide el efecto sobre la sesión; Simulation crea el mundo y evalúa la victoria gameplay.
- El cierre normal intenta vaciar mensajes esenciales sin bloquear indefinidamente; timeout de drenaje configurable en Networking.

## 10. Pruebas de aceptación

**DECIDIDO:** P1 y P2 asignados por conexión; tercero rechazado; espera ilimitada con un jugador; no ticks sin dos Ready; Ready duplicado/viejo no inicia; antes de Running se libera plaza por desconexión y se invalida inicialización; nuevo jugador genera snapshot nuevo; timeout Ready aborta intento sin iniciar ticks; desconexión P1 en Running → gana P2; doble desconexión en mismo lote sin resultado → técnico; doble destrucción de bases → empate; resultado ya fijado no cambia; mensajes tras Ended no se aplican; eventos de sesión concurrentes no corrompen estado.

**RECOMENDADO:** usar test de máquina de estados sin sockets, con secuencias de eventos controladas, y pruebas combinadas con Networking para orden de `MatchEnded`.

## 11. Configuración — DECIDIDO / PENDIENTE

**DECIDIDO:** timeout configurable de Ready; espera por segundo jugador ilimitada; máximo de dos clientes. **PENDIENTE:** valor exacto de ReadyTimeout, política de expulsión al timeout y duración concreta de drenaje de salida.

## 12. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** máquina de estados desacoplada de `TcpClient` y `WorldState`; recibir eventos serializados y emitir acciones tipadas para un coordinador/orquestador.
2. **Modelos:** `GameSessionState` separado de `MatchState`/`WorldState`; `PlayerSession` solo guarda identificadores y confirmaciones; finalizaciones inmutables.
3. **Interfaces/DTOs:** `SessionEvent` y `SessionTransitionResult` tipados con acciones `SendMessage`, `StartSimulation`, `StopSimulation`, `CloseConnection`; hacer finales idempotentes.
4. **JSON:** `ClientReady` con InitializationId; `MatchStarted/Ended` con SessionId/resultado; `PlayerAssigned` sin confiar en identidad aportada por el cliente.
5. **Configuración:** ReadyTimeout, límite de cierre y políticas de inactividad por entorno; usar monotonic timer para plazos prepartida.
6. **Organización del trabajo:** desarrollar primero reducer/máquina de estados probada sin red; integrar a continuación TCP + InitialState; último paso, resultados de Simulation y desconexiones concurrentes.

## 13. Pendientes explícitos

**PENDIENTE:** contratos de transición, política precisa al expirar ReadyTimeout (conservación/expulsión), precedencia fina entre eventos simultáneos de sesión/simulación, DTOs JSON y valores de timeout. No convertir recomendaciones en reglas sin aprobación.
