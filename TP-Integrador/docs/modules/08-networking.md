# 08 — Networking

> **Estado:** protocolo conceptual y políticas de conexiones aprobadas; esquemas JSON exactos y valores de límites pendientes.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Simulation](06-simulation.md), [GameSession](07-game-session.md), [State Sync](09-state-sync.md), [Units / Movement](04-units-movement.md).

## 1. Propósito y transporte — DECIDIDO

Networking administra **conexiones TCP** entre dos clientes Godot y un servidor .NET, transporta mensajes, valida formato/protocolo, distribuye órdenes/eventos y supervisa conectividad. **No interpreta ni ejecuta mecánicas** de gameplay y no escribe `WorldState` ni `GameSessionState`.

- **TCP + JSON en UTF-8**.
- **Un objeto JSON completo por línea**: delimitador `\n`.
- Un servidor/proceso maneja **una partida y como máximo dos clientes admitidos**; un tercero se rechaza mediante decisión de GameSession.
- Sin reconexión, matchmaking, espectadores o múltiples sesiones.

## 2. Framing y lectura TCP — DECIDIDO

TCP es **flujo de bytes**, no transporte de mensajes. Una lectura puede contener un fragmento de un JSON, varios mensajes completos o partes de dos mensajes. Por ello, el receptor por cliente:

1. Lee bytes de manera asíncrona y acumula hasta encontrar delimitador de línea.
2. Reconstruye cada trama completa sin asumir relación lectura=mensaje.
3. Valida tamaño en **bytes** (máximo configurable), codificación UTF-8, JSON, versión y campos obligatorios.
4. Clasifica el mensaje y lo entrega al destino correspondiente.
5. Ante **JSON malformado**, rechaza **ese mensaje** con `ProtocolError` y **mantiene la conexión**. Abuso repetido o exceso de tamaño pueden disparar política de seguridad/desconexión.

**DECIDIDO:** clientes con versión de protocolo **incompatible** se rechazan con error de versión y no se aceptan para gameplay. No se implementa negociación de múltiples versiones.

**RECOMENDADO:** handshake explícito con mensaje `Hello` que valide `ProtocolVersion` antes de registrar definitivamente al jugador; todavía no se aprobó tipo/nombre exacto del handshake.

## 3. Contratos de red — PROPUESTOS, NO JSON DEFINITIVO

| Campo `NetworkMessage` | Descripción |
|---|---|
| `ProtocolVersion` | Versión entera común entre Godot y .NET |
| `Type` | Tipo de mensaje |
| `MessageId` | Correlación opcional; **obligatoria para gameplay** como ClientCommandId |
| `Payload` | Objeto concreto dependiente del tipo |

**Categorías:**

| Flujo | Mensajes conceptuales | Destino |
|---|---|---|
| Sesión | `PlayerAssigned`, `ClientReady`, `MatchStarted`, `MatchEnded` | GameSession / cliente |
| Gameplay | `Move`, `Stop`, `Attack`, `GatherWood`, `Build`, `RepairFarm`, `TrainUnit` | Command Admission → Simulation |
| Rutas | `RepathRequest`, `RepathResponse` | Movement / Simulation, vía State Sync y Networking |
| Resultados | `CommandResult`, `ProtocolError` | Cliente originador |
| Estado | `InitialState`, `StateEvents`, `PositionUpdate` | Cliente vía State Sync |
| Conectividad | `Ping`, `Pong` | Networking |

`PlayerId` **se determina por la conexión registrada** y nunca se confía en una identidad escrita libremente por Godot. No usar entidades de dominio como objetos JSON directos; usar DTOs de protocolo.

## 4. Recepción concurrente y admisión — DECIDIDO

- Una tarea receptora asíncrona por conexión; ambas pueden entregar comandos simultáneamente.
- Un punto seguro de **Command Admission** asigna `Sequence` global y encola **en el mismo orden**; evitar asignar secuencia y posteriormente insertar sin coordinación.
- Simulation es **único consumidor** de la cola de comandos y procesa lote máximo configurable por tick; comandos admitidos sobrantes esperan.
- **Límite de admisión por cliente/segundo configurable**: rechazar solicitudes excedentes nuevas, sin quitar las ya admitidas. Abuso persistente puede terminar conexión.
- La clave `(PlayerId, ClientCommandId)` permite detectar duplicados encolados/procesados; Simulation es dueño del resultado oficial y conserva idempotencia de negocio.
- Los receptores no validan madera, combate ni ruta autoritativa; sí tipo de mensaje y campos estructurales, para enrutar correctamente.
- Los mensajes de sesión se entregan a la cola serializada de GameSession, no a la cola de gameplay.

## 5. Escritura concurrente y aislamiento — DECIDIDO

- Cada cliente tiene **cola de salida acotada** y un **escritor único independiente** (`SendLoop`) para su conexión.
- State Sync y GameSession producen publicaciones; no escriben bytes directamente sobre el socket.
- El escritor de un cliente lento **no bloquea** al otro ni al loop de Simulation.
- Los mensajes de salida para un cliente deben conservar orden lógico; se intercalan adecuadamente `PlayerAssigned`, `InitialState`, `MatchStarted`, eventos, posiciones y `MatchEnded`.
- Eventos **esenciales** (creación, destrucción, recursos propios, resultado, etc.) no se eliminan silenciosamente. State Sync puede consolidar posiciones redundantes **antes de la cola definitiva** si respeta dependencias.
- Si la cola del cliente permanece saturada, aplicar política configurable y **desconectar** en vez de consumir memoria ilimitada.
- Durante cierre normal, intentar enviar eventos esenciales/resultados pendientes con un tiempo máximo finito; no hay garantía de entrega a sockets ya muertos.
- La escritura TCP nunca se realiza dentro del tick de Simulation.

## 6. Heartbeat y desconexiones — DECIDIDO

- `Ping` se emite **cuando una conexión permanece inactiva** durante intervalo configurable, no constantemente mientras recibe tráfico válido.
- `Pong` confirma la vida de esa conexión y debe corresponder al Ping pendiente; ante timeout configurable se considera perdida.
- La inactividad pertinente es **entrada válida desde ese cliente**: enviar mensajes desde el servidor por sí solo no demuestra que el cliente responda.
- Heartbeat y supervisión no dependen de ticks de gameplay: operan también durante la espera de jugadores y durante `Starting`.
- Timeout debe ser prudente: durante Running, la desconexión de uno hace ganar al otro por decisión de GameSession.
- Networking reporta `Disconnected` con identidad de conexión; GameSession decide victoria, espera o cierre técnico.
- **RECOMENDADO:** tiempo monotónico y un identificador correlativo por Ping. La forma exacta del DTO Ping/Pong aún no fue aprobada.

## 7. Contratos/componentes internos — PROPUESTOS

| Componente/operación | Función |
|---|---|
| `TcpServer` / `StartListening` | Escuchar y aceptar conexiones |
| `ClientConnection` | Vida de una conexión, lectura, escritura, heartbeat y cierre |
| `MessageDispatcher` | Validar estructura/clasificar y entregar a consumidor |
| `ReceiveLoop` | Acumular bytes y reconstruir JSON por línea |
| `ParseMessage` | Validación UTF-8, framing, versión y payload estructural |
| `DispatchMessage` | Enrutar a GameSession/Command Admission/heartbeat |
| `EnqueueOutbound` | Admisión de publicación a cola de cliente |
| `SendLoop` | Serializar/envíar con un solo escritor |
| `MonitorConnections` | Gestionar intervalos y timeouts |
| `CloseConnection` | Liberar recursos e informar desconexión una vez |

No implican nueve clases obligatorias. La implementación .NET puede usar I/O asíncrona y canales/colas thread-safe; **no requiere thread dedicado por mensaje**.

## 8. Concurrencia y garantías — DECIDIDO

- Dos receptores concurrentes producen comandos; acceso sincronizado a secuencia/cola asegura orden.
- Cada cliente posee único escritor; ningún mensaje puede intercalar sus bytes con otro.
- Los mensajes de dos jugadores no se mezclan en un socket compartido: cada uno mantiene conexión propia.
- Mensajes de gameplay antes de `Running` se rechazan por política de sesión; no modifican mundo.
- Protocol errors recuperables no implican necesariamente desconexión.
- Versiones incompatibles no inician partida.
- La capacidad acotada protege memoria; una saturación persistente provoca desconexión definida.

## 9. Pruebas de aceptación

**DECIDIDO:** TCP fragmenta mensaje en varias lecturas; una lectura trae varios mensajes; JSON UTF-8 inválido/JSON malformado no produce ejecución y permite continuar; tamaño extremo controlado; versión incompatible rechazada; tercero rechazado; recepción simultánea → secuencias únicas y en orden; duplicado idempotente; flood per-client rechaza nuevas admisiones; un escritor por conexión evita mezclas; cliente lento no bloquea el otro; cola saturada desconecta; Ping tras inactividad; Pong válido mantiene; timeout notifica a GameSession; PlayerId falsificado ignorado; ticks prosiguen durante send lento.

**RECOMENDADO:** stress test de 500 mensajes por jugador con límites deliberadamente amplios; inspección de admisiones, rechazo de flood y métricas de colas; tests con red local simulada y fallos de socket.

## 10. Configuración — DECIDIDO / PENDIENTE

**Configurables aprobados:** dirección/puerto de escucha, versión protocolo, máximo bytes por mensaje, límite de admisión por cliente/intervalo, capacidad de cola de salida, política de saturación, intervalo idle Ping, timeout Pong, tiempo de drenaje/cierre. **PENDIENTE:** valores exactos; definición de abuso persistente; mensajes/errores exactos; política de backpressure interna.

## 11. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** aislar `TcpServer`, `ClientConnection` y `MessageDispatcher`; no referenciar directamente Economy/Combat; disponer una interfaz de eventos para GameSession y otra de admisión para Simulation.
2. **Modelos:** `ConnectionId`, `RegisteredPlayerId`, estado del heartbeat, contadores de rate-limit, cola outbound; separar de modelos de `WorldState`.
3. **Interfaces/DTOs:** `NetworkEnvelope`, mensaje validado, `ProtocolErrorCode`, `CommandEnvelope` (tras admisión), `OutboundMessage`; centralizar serialización y versionado para Godot/C#.
4. **JSON:** definir versión fija, casing, enums/strings, delimitador `\n`, IDs, coordenadas, tipos de mensajes y errores en especificación única; pruebas de round-trip entre C# y Godot.
5. **Configuración:** configurar límites pequeños pero seguros para pruebas, monitorear tamaño de colas y bytes, distinguir timeout técnico de overrun de tick.
6. **Organización del trabajo:** implementar framing y parsing con tests, después doble recepción/cola, después escritores y heartbeat; integrar GameSession y un Simulation fake antes de conectar gameplay real.

> **Nota (2026-11-10):** los asuntos de este módulo se formalizaron en el [contrato 08](../contracts/08-networking.md). Quedaron DECIDIDOS: plaza recién tras un `Hello` válido (D-053), desconexión inmediata si un esencial no entra en la cola (D-073), abuso por N faltas en una ventana o por exceso de tamaño (D-074) y `CommandRejected` para los rechazos previos a la admisión (D-075). El handshake, el límite por segundo, los hilos y el heartbeat quedaron como RECOMENDADO (D-076 a D-080). Los DTOs viven en `Rts.Protocol` (D-066).

## 12. Pendientes explícitos

**PENDIENTE:** definiciones JSON definitivas, handshake de versión, códigos de error, valores de límites, coordinación de publicación/cola y detalles de cierre; el transporte elegido y las políticas aprobadas no se pueden cambiar unilateralmente.
