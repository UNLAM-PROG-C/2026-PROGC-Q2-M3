# Contrato 10 — Server (composición)

> **Estado:** esqueleto implementado y probado (`server/src/Rts.Server`, `server/tests/Rts.Server.Tests`). Están completos: configuración, validación cruzada, catálogo, adaptador de efectos, códigos de salida y apagado ordenado. La creación de las piezas (§4) se completa en la vertical slice, cuando existan las implementaciones de los módulos.
> **Etiquetas:** **DECIDIDO** = aprobado por el equipo (ver [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **No es un módulo:** no tiene reglas de juego ni lógica de interacción. Es el `main()` del servidor (contrato 00 §1), y el único proyecto que referencia a todos.

## 1. Qué hace y qué no

Se ejecuta **una vez** por proceso:

1. lee los argumentos;
2. lee y valida la configuración completa, incluidas las validaciones cruzadas;
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
  - **Logging:** el de consola estándar de .NET (`Microsoft.Extensions.Logging.Console`), que escribe en segundo plano y no frena el tick.
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
| 4 | **Temporal:** composición todavía sin implementaciones (§6) |

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
| 5 | Mapa, `SimulationEngine` (con los 5 módulos de dominio) y `SimulationLoop` | 1, 4 (como `ITickPublisher`), buzón (como `IMatchResultSink`) | ticks |
| 6 | `SessionEffectsAdapter` y `SessionHost` | 2, 3, 4, 5 (como `ISimulationControl`) | máquina de estados de la sesión |

**Arranque y fin:**

1. `network.RunAsync` empieza a aceptar conexiones en segundo plano.
2. `await session.RunAsync(shutdown)` corre hasta `Ended`. Para entonces la sesión ya detuvo los ticks y drenó las conexiones.
3. Se espera que termine la aceptación.
4. Se devuelve el resultado.

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

- `ServerComposition.RunMatchAsync` documenta en código el orden de §4 y por ahora lanza `NotImplementedException`; el programa sale con el código 4.
- `rts-server --validate-only` ya funciona: carga, valida, arma el catálogo y muestra el resumen.
- **Listo del lado de Simulation:** la cola de comandos (`CommandQueue`), el cargador de mapa (`MapLoader`) y la fábrica `SimulationFactory` (`CreateEngine`, `CreateLoop`), que reciben los cinco módulos de dominio agrupados en `DomainModules` (D-089).
- **PENDIENTE:** las implementaciones que faltan para completar la composición:
  - buzón y vista de sesión, y `SessionHost` (GameSession);
  - `NetworkServer` (Networking) y `StateSyncPublisher` (State Sync);
  - las implementaciones de reglas y calculadores de los cinco módulos de dominio.

  Se agregan en la vertical slice (AGENTS §8).
- **PENDIENTE:** el archivo de mapa real (formato D-052) y los valores definitivos de configuración.
