# Contrato 10 — Server (composición)

> **Estado:** implementado y probado (`server/src/Rts.Server`, `server/tests/Rts.Server.Tests`). El servidor arma las seis piezas reales en el orden de §4 y corre una partida completa; una prueba de punta a punta con dos clientes TCP la recorre entera (§6). Están completos también la configuración, la validación cruzada y la carga del mapa, el catálogo, el adaptador de efectos, el diagnóstico de Simulation (D-100), los códigos de salida y el apagado ordenado. **Provisorio:** Production, hasta que exista el módulo real (§6).
> **Etiquetas:** **DECIDIDO** = aprobado por el equipo (ver [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **No es un módulo:** no tiene reglas de juego ni lógica de interacción. Es el `main()` del servidor (contrato 00 §1), y el único proyecto que referencia a todos.

## 1. Qué hace y qué no

Se ejecuta **una vez** por proceso:

1. lee los argumentos;
2. lee y valida la configuración completa, incluidas las validaciones cruzadas, y carga el mapa (un mapa inválido también sale con el código 2);
3. arma el catálogo;
4. crea las piezas en orden y las conecta;
5. arranca la partida;
6. espera a que termine;
7. sale con el código del resultado.

**No** es el bucle del juego: el loop de ticks a 20 Hz vive en Simulation (contrato 06 §6). Tampoco participa de la fluidez durante la partida: después del arranque, el Server solo espera. Lo que hace fluido al juego está en otros lados:

- ticks por debajo de 50 ms (Economy ∥ Combat, sin I/O en el tick);
- posiciones a 10 Hz con interpolación en Godot;
- colas de salida independientes por cliente.

## 2. Decisiones

- **DECIDIDO (D-081): composición manual con `new`, sin contenedor de dependencias ni host genérico.**
  - El orden de construcción se lee en el código, de arriba hacia abajo.
  - Si falta una dependencia, no compila.
  - En ejecución rinde igual que con un framework, porque la composición ocurre una sola vez.
- **DECIDIDO (D-082): "buzones primero".**
  - Las piezas que solo reciben se crean primero: cola de comandos, buzón y vista de sesión.
  - Así cada pieza encuentra ya creadas todas las que necesita, y no hay objetos conectados "más tarde".
  - La comunicación en ambos sentidos entre módulos (la red trae comandos y lleva resultados) pasa siempre por colas. Nadie espera a nadie ni toma el lock de otro módulo, así que no hay riesgo de bloqueos cruzados.
  - Las referencias entre proyectos siguen siendo un árbol sin ciclos, y el compilador lo hace cumplir.
- **RECOMENDADO (D-083): configuración y logging.**
  - **Configuración:** un `server.json` con una sección por módulo, leído con el lector JSON de .NET. La ruta del mapa es relativa al archivo de configuración. Por línea de comandos se pueden pasar `--config`, `--port` y `--validate-only`.
  - **Logging:** el de consola estándar de .NET (`Microsoft.Extensions.Logging.Console`), que escribe en segundo plano y no frena el tick. El Server es el único que escribe: traduce a `ILogger` el diagnóstico de Simulation (DECIDIDO D-095) y les pasa `ILogger<T>` a GameSession y Networking (RECOMENDADO D-096).
  - **Diagnóstico de Simulation (DECIDIDO D-100):** lo escribe `LoggingSimulationDiagnostics` (`Rts.Server.Diagnostics`), categoría `Rts.Simulation`. Qué se escribe lo decide solo `SimulationConfig.Diagnostics`; por eso `Program` deja pasar esa categoría en todos los niveles. La composición lo crea junto al loop y llama `WriteFinalSummary()` cuando la sesión terminó.
- **DECIDIDO (D-084): apagado ordenado.**
  - El primer Ctrl+C (o SIGTERM) cancela `RunAsync`, y GameSession lo recibe como `ShutdownRequested`.
  - Si no hay otro resultado, la partida termina como final técnico con motivo nuevo `ServerShutdown`, sin declarar un "error" que no ocurrió.
  - En Running se detienen los ticks antes de fijar el resultado (contrato 07 §3).
  - Un segundo Ctrl+C corta el proceso en seco.
- **RECOMENDADO (D-085): códigos de salida.**

| Código | Significado |
|---|---|
| 0 | Resultado oficial que no es error (incluido `ServerShutdown`), o `--validate-only` sin problemas |
| 1 | `CriticalError` (D-058) |
| 2 | Configuración ilegible o inválida: no arrancó |
| 3 | Argumentos inválidos |
| 4 | **Retirado** (era «composición pendiente», temporal). No se reutiliza |
| 5 | La red no pudo escuchar (por ejemplo, el puerto está ocupado): no arrancó |

## 3. Configuración

`ServerConfig` = `mapFile` + una sección por módulo, cada una con la configuración tipada que ya define su módulo. El Server no agrega parámetros de juego. `server/config/server.example.json` es un ejemplo completo con **valores temporales**; los definitivos son PENDIENTES de balance.

**Lectura (`ServerConfigLoader`):**

- Propiedades en camelCase, enums como string, comentarios `//` y comas finales permitidos.
- Los `TimeSpan` se escriben como `"hh:mm:ss.fff"`.
- **Un campo desconocido es error**, para que un nombre mal escrito no pase en silencio con el valor por defecto. Un campo obligatorio que falta, también.
- Las huellas usan un convertidor propio del Server, para que `Rts.Shared` siga sin atributos JSON.

**Validación (`ConfigurationValidator`)**, todo antes de crear nada. Con cualquier error, el servidor no arranca y sale con el código 2:

| Validación | Origen |
|---|---|
| Cada sección con su propio `GetValidationErrors()` | cada módulo |
| `stateSync` contra `simulation.tickRate`: la cadencia debe dividir exacto | cruzada, D-062 |
| `movement.attackRange` = `combat.soldier.range` | cruzada, D-030 |
| Existe el archivo de mapa | Server |

El contenido del mapa lo valida Simulation al cargarlo (`MapDefinition.GetValidationErrors`, contrato 06 §8).

**Catálogo (`GameCatalogBuilder`, D-071):** copia cada valor de su módulo dueño. Cada número tiene una sola fuente:

- HP de unidades: Combat. HP y huella de edificios: Construction.
- Costos: Economy. Duración de entrenamiento: Production. Velocidad y rangos: Movement.
- Ataques del soldado y la torre: Combat.

## 4. Orden de construcción

Cada paso usa solo lo creado en los anteriores (D-082):

| # | Pieza | Recibe | Rol |
|---|---|---|---|
| 1 | Cola global de comandos (Simulation) | capacidad | `ICommandSink` para Networking, `ICommandSource` para Simulation |
| 2 | Buzón y vista de sesión (GameSession) | capacidad, estado inicial | `ISessionEventSink` + `IMatchResultSink`; `ISessionGate` |
| 3 | `NetworkServer` | 1, 2, configuración | recibe, admite, escribe |
| 4 | `StateSyncPublisher` | 3 (como `IClientOutbox`), catálogo | proyecta y encola |
| 5 | `SimulationEngine` (con el mapa ya cargado y los 5 módulos de dominio) y `SimulationLoop` | 1, 4 (como `ITickPublisher`), buzón (como `IMatchResultSink`) | ticks |
| 6 | `SessionEffectsAdapter` y `SessionHost` | 2, 3, 4, 5 (como `ISimulationControl`) | máquina de estados de la sesión |

**Arranque y fin:**

1. `network.RunAsync` empieza a aceptar conexiones en segundo plano. **No recibe el token de apagado:** deja de aceptar cuando la sesión pide `DrainAndCloseAllAsync`, que siempre ocurre antes de `Ended`. Así el primer Ctrl+C no corta la aceptación ni el heartbeat antes del drenaje ordenado.
2. `await session.RunAsync(shutdown)` corre hasta `Ended`. Para entonces la sesión ya detuvo los ticks y drenó las conexiones. Si la red falla antes (por ejemplo, el puerto está ocupado), la composición pide el apagado ordenado de la sesión en vez de esperar jugadores para siempre.
3. Se escribe el resumen final del diagnóstico de Simulation (D-100).
4. Se espera que termine la aceptación. Si había fallado, su error sale de `RunMatchAsync` y el proceso sale con el código 5.
5. Se devuelve el resultado.

**Hilos en ejecución** (lo que se muestra en la defensa):

| Hilo / tarea | Qué hace |
|---|---|
| Loop de Simulation (hilo dedicado) | ticks a 20 Hz |
| Economy y Combat (2 tareas por tick) | fase 5 en paralelo y barrera |
| Consumidor de sesión | máquina de estados de GameSession |
| Networking | aceptación, un receptor y un escritor por cliente, monitor de heartbeat |
| Principal | espera el fin y sale |

## 5. Adaptador de efectos

`SessionEffectsAdapter` implementa el puerto `ISessionEffects` de GameSession y solo delega, sin agregar reglas:

| Método | Destino |
|---|---|
| `Send` | `INetworkServer.Send` |
| `Close` | `INetworkServer.Close` |
| `DrainAndCloseAllAsync` | `INetworkServer.DrainAndCloseAllAsync` |
| `PublishInitialState` | `IStateSyncPublisher.PublishInitialState`; State Sync dirige por `PlayerId`, D-069 |

## 6. Estado actual y pendientes

**Composición (2026-11-16).** `ServerComposition.RunMatchAsync` arma las piezas en tres grupos, uno por método, cada uno probado por separado:

| Método | Piezas de §4 | Devuelve |
|---|---|---|
| `CreateTransport` | 1 a 4: cola de comandos, buzón y vista de sesión, `NetworkServer`, `StateSyncPublisher` | `TransportPieces` |
| `CreateSimulation` | 5: motor con los cinco módulos de dominio (`CreateDomainModules`) y loop, con `LoggingSimulationDiagnostics` | `SimulationPieces` (loop y diagnóstico) |
| `CreateSession` | 6: `SessionStateMachine`, `SessionEffectsAdapter` y `SessionHost` | `ISessionHost` |

Crear no arranca nada: la red escucha recién en `RunAsync` y los ticks los arranca GameSession con `StartTicks`. Después corre la secuencia de «Arranque y fin» de §4.

**Pruebas** (`server/tests/Rts.Server.Tests`):

- `Composition/ServerCompositionTests`: cada grupo por separado, el apagado sin jugadores (`ServerShutdown`) y el puerto ocupado (código 5).
- `EndToEnd/FullMatchTests`: **prueba de punta a punta.** Dos clientes TCP que hablan JSON crudo por líneas, como Godot: `Hello` → `PlayerAssigned` → `InitialState` → `ClientReady` → `MatchStarted` → un `Stop` y un `TrainUnit` aceptados, de ida y vuelta → Ctrl+C → `MatchEnded` antes que `Disconnecting` → cierre.

**Production real (2026-11-16):** `CreateDomainModules` arma `ProductionRules` y `ProductionCalculator`, y le pasa al calculador `EconomyConfig.UnitFoodCosts` (D-015, contrato 05 §1). Reemplazó al provisorio `UnavailableProduction` y al stub de `Rts.Integration.Tests` y `Rts.Measurement`, que se borraron.

**PENDIENTE:**

- Prueba manual con el cliente Godot real: dos instancias contra `rts-server` (08 §11).
- Logging de `SessionHost`: se le pasará `ILogger<T>` si se acepta la D-096 (O-011).
- El archivo de mapa real (formato D-052) y los valores definitivos de configuración.
