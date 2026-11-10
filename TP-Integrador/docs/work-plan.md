# Plan de trabajo — reparto y fases

> **Para:** Agus, Luca, Flor, Ivan, Coro y Joaco.
> **Qué es:** quién hace qué, en qué orden y cómo trabajamos juntos. **No** explica los módulos: para eso están los contratos (`docs/contracts/`), que dicen qué construir, qué recibe y devuelve cada pieza, qué esperan los vecinos y qué pruebas hacen falta.
> **Antes de empezar:** leer `AGENTS.md`, el contrato de cada módulo propio y la sección "lo que deben hacer los demás" de los contratos vecinos.

## 1. Reparto

Los módulos se agrupan por afinidad y por tamaño: no son todos iguales, así que nadie toma "un módulo" a secas.

| Quién | Es dueño de | Contratos | Por qué juntos |
|---|---|---|---|
| **Luca** | Simulation + Server; integración; núcleo compartido (`Rts.Shared`) y proceso de contratos | 00, 06, 10 | Simulation es el centro: todos los módulos pasan por su tick. Quien arma la arquitectura integra |
| **Flor** | Networking + State Sync; dueña del esquema `Rts.Protocol` y de sus ejemplos | 08, 09 | Todo lo que viaja por el cable, en ambos sentidos. Una sola persona de contacto para Godot |
| **Ivan** | Movement + Construction | 04, 03 | "El mapa": grilla, rutas, alcanzabilidad, ocupación y colocación |
| **Coro** | Combat + Economy | 02, 01 | Son los dos módulos que corren **en paralelo** en la fase 5; Coro es dueña de la prueba de determinismo de esa fase |
| **Joaco** | GameSession + Production | 07, 05 | Dos máquinas de estado con colas: la vida de la partida y la cola de entrenamiento |
| **Agus** | Cliente Godot | 08 §2, 09, 04 §2 | Ya venía trabajando en Godot |

**La asignación de Flor, Ivan, Coro y Joaco es tentativa:** pueden intercambiar los grupos según preferencias o fortalezas. Lo importante es que cada grupo tenga un dueño.

**Ser dueño** significa:
- implementar la lógica y sus pruebas;
- revisar todo PR que toque esos módulos;
- explicarlos en la defensa;
- ser la persona a quien se le pregunta.

Lo que hace Agus en Godot está en §2.

## 2. Godot (Agus)

El cliente Godot está **desacoplado del servidor**: la única unión entre los dos es el protocolo (contratos 08 §2 y 09). Por eso Agus puede avanzar en paralelo desde el día 1, programando contra los ejemplos de `docs/protocol/examples/`. Sus contactos principales son **Flor** (protocolo) e **Ivan** (rutas y navegación).

### Qué construye

1. **Cliente del protocolo:** TCP, una línea JSON por mensaje, el sobre, `Hello`, `Ping`/`Pong` y `ClientReady`.
2. **Mundo:** construir el mundo desde el `InitialState`, aplicar los `StateEvents` en orden e interpolar las `PositionUpdate`, sin extrapolar (contrato 09 §6).
3. **Navegación:** NavigationMesh y **rutas candidatas** para cada comando de movimiento. Volver a armar la navegación con cada `MapRevisionChanged` y responder los `RepathRequested`.
4. **Interfaz:**
   - selección e input → comandos, con `messageId` creciente;
   - manejar `CommandRejected` (se puede reintentar) y `CommandResult` (definitivo);
   - HUD con los recursos y la cola de producción propios;
   - vista previa de colocación con el catálogo;
   - respuesta inmediata solo visual al hacer clic;
   - pantallas de `MatchStarted`, `StartAborted` y `MatchEnded`.

### Ejemplos del protocolo

`docs/protocol/examples/` tiene un archivo por cada mensaje, **generado desde los DTOs reales**: si un mensaje de ejemplo se procesa bien, el del servidor real también. Las reglas de lectura están en el [README de la carpeta](protocol/examples/README.md):
- cada mensaje es una línea en el cable;
- un campo ausente vale `null`;
- todos son el mismo mundo visto por P1;
- los números son temporales.

**Lo que recibe Godot** (`server-to-client/`). Están en orden de partida y se pueden aplicar uno tras otro sobre el mundo del archivo 02:

| Archivo | Qué permite probar |
|---|---|
| `01-player-assigned` | Guardar el `playerId` propio. Es la identidad para toda la partida |
| `02-initial-state` | Construir el mundo completo: mapa, unidades, edificios, árboles, recursos y cola propios, catálogo |
| `03-start-aborted` | Descartar el mundo cargado y volver a la espera (flujo alternativo: después llegaría otro `InitialState`) |
| `04-match-started` | Pasar a la partida; recién ahí se habilitan los comandos |
| `05-state-events-orders-accepted` | Aplicar un lote: orden de eventos por `index` y dos `commandResults` aceptados, correlacionados por `clientCommandId` |
| `06-position-update` | Interpolar posiciones; ignorar las de un tick viejo y las de IDs desconocidos |
| `07-state-events-building-placed` | Instanciar una obra, actualizar la navegación con `MapRevisionChanged` y descontar madera con `ResourcesChanged` |
| `08-state-events-repath-requested` | Responder con una ruta nueva (ver `client-to-server/repath-response`) |
| `09-state-events-production-started` | Mostrar la cola de producción propia y el tiempo restante |
| `10-state-events-command-results` | Resultado parcial (una unidad rechazada por no ser propia) y rechazado; mostrar el error al jugador |
| `11-state-events-building-completed` | Barra de construcción hasta el máximo, edificio terminado y constructor en reposo |
| `12-state-events-unit-spawned` | Crear una unidad nueva **antes** de recibir su primera posición |
| `13-state-events-tree-depleted` | Ocultar un árbol sin borrarlo (conserva su ID) y dejar al recolector en reposo |
| `14-state-events-farm-depleted` | Mostrar la granja agotada |
| `15-state-events-farm-repair-started` | Granja en reparación, orden de reparar y cobro |
| `16-state-events-farm-repair-completed` | Progreso de reparación y granja de vuelta en producción |
| `17-state-events-tree-respawned` | Volver a mostrar el mismo árbol |
| `18-state-events-entity-damaged` | Actualizar la barra de vida con el HP recibido |
| `19-state-events-entity-destroyed` | Eliminar una unidad y un edificio; con el edificio cambia el mapa (navegación) |
| `20-command-rejected` | Rechazo antes de entrar al juego: se puede reintentar con el mismo `messageId` |
| `21-protocol-error` | Un mensaje propio mal formado: la conexión sigue abierta |
| `22-protocol-error-unsupported-version` | Versión incompatible: mostrar el error (después llega `Disconnecting`) |
| `23-session-request-rejected` | Un `ClientReady` viejo: no tiene efecto |
| `24-ping` / `25-pong` | Responder el `Ping` del servidor con `Pong`; recibir el `Pong` a un `Ping` propio |
| `26`, `27`, `28-match-ended-*` | Pantallas de victoria, empate y victoria por desconexión del rival |
| `29-disconnecting` | El servidor cierra: mostrar el motivo y volver al menú |

**Lo que envía Godot** (`client-to-server/`): `hello`, `client-ready`, `ping`, `pong` y un ejemplo de cada comando (`move`, `stop`, `attack`, `gather-wood`, `build-place`, `build-continue`, `repair-farm`, `train-unit`, `repath-response`). Sirven para verificar que Godot arma exactamente ese formato:
- **`messageId`:** obligatorio en los comandos y creciente.
- **Rutas:** una ruta candidata por unidad, en metros X/Z.
- **Identidad:** ningún mensaje lleva `playerId`.

### Idea para más adelante: servidor falso

Un programa chico que hable el protocolo de verdad sobre TCP. Aceptaría el `Hello`, enviaría `PlayerAssigned`, `InitialState` y `MatchStarted`, reproduciría los ejemplos a 20 Hz y movería en línea recta las unidades de un `Move`. Le permitiría a Agus probar su código de red antes de que exista la vertical slice. No se hace por ahora; se evalúa si hace falta.

## 3. Fases

Las fases no tienen fecha: pónganla en la primera reunión. Los módulos de dominio son funciones puras, así que **todos arrancan en paralelo desde el día 1**.

### Fase 0 — Puesta en marcha

Todos:
- instalan el SDK de .NET 10 y corren `dotnet test server/Rts.slnx`;
- leen sus contratos;
- acuerdan cómo trabajar con ramas y revisiones (§5).

### Fase 1 — Vertical slice

**Objetivo:** dos clientes Godot se conectan, pasan por `Ready` y `MatchStarted`, uno manda un `Move` y ambos ven moverse la unidad.

| Quién | Qué |
|---|---|
| Luca | Cola de comandos, motor con las fases 1, 2 y 9 (solo `Move`), loop de 20 Hz, cargador de mapa, composición del Server |
| Flor | Framing, parser (`Hello`, `ClientReady`, `Ping`/`Pong`, `Move`), admisión, cola de salida y `SendLoop`; State Sync con `InitialState`, `StateEvents` y `PositionUpdate`. Mantener al día `docs/protocol/examples/` cuando cambie un DTO (§2) |
| Joaco | GameSession completo: la máquina de estados según la tabla del contrato 07 §3, el buzón, la vista y el host |
| Ivan | Movement mínimo para `Move`: validar la ruta, evaluar la orden y avanzar por la ruta |
| Coro | Economy y Combat completos con pruebas unitarias. Son puros y no los necesita la vertical slice |
| Agus | Cliente del protocolo, carga del `InitialState`, `Move` con ruta candidata, interpolación. Hasta que haya servidor, trabaja con `docs/protocol/examples/` (§2) |

### Fase 2 — Dominio completo

| Quién | Qué |
|---|---|
| Luca | Fases 3, 5, 6, 7 y 8 del tick: commits, cascadas y orden de eventos |
| Ivan | Resto de Movement (persecución, repath, BFS de alcanzabilidad) y Construction completo |
| Coro | Integra Economy ∥ Combat con Luca: tareas paralelas, barrera y prueba de determinismo |
| Joaco | Production completo e integración en la fase 8 |
| Flor | El resto de los comandos en el parser, todos los eventos en State Sync y la política de visibilidad |
| Agus | Construir, recolectar, atacar, reparar y entrenar; HUD; vista previa; respuestas de repath |

### Fase 3 — Robustez

| Quién | Qué |
|---|---|
| Flor | Heartbeat, abuso, saturación y drenaje |
| Joaco | ReadyTimeout, desconexiones en cada estado y apagado |
| Luca | Métricas de ticks, sobrecarga y fallas críticas |
| Todos | Pruebas de integración de AGENTS §7 (§4 de este plan) |

### Fase 4 — Cierre

- **Balance:** Coro, Ivan y Joaco proponen los números y el equipo los aprueba. Se registran en `decisions.md` y reemplazan los valores temporales de `server/config/server.example.json`.
- **Mapa:** Agus e Ivan arman el mapa (formato D-052).
- **Defensa:** prepararla (§6).

## 4. Pruebas de integración (AGENTS §7)

| Prueba | Responsable |
|---|---|
| Dos jugadores con comandos concurrentes: secuencias únicas, en orden, sin duplicados | Flor + Luca |
| Colocación simultánea sobre una celda: una sola aceptada, un solo cobro | Ivan + Luca |
| Economy y Combat terminan en distinto orden: mismo resultado | Coro + Luca |
| Destrucción simultánea de ambas bases: empate | Coro |
| Recolección y muerte, o granja que produce y se destruye, en el mismo tick: recurso acreditado | Coro |
| Dos apariciones no eligen la misma celda | Joaco |
| Cliente lento: no bloquea Simulation ni al otro cliente | Flor |
| Desconexiones y `MatchEnded` concurrentes: un solo resultado | Joaco |
| Eventos por cliente: mismo mundo público, datos privados aislados | Flor |

## 5. Cómo trabajamos

- **Ramas:** una por tema, `feat/<módulo>-<tema>`, con PR hacia `main`. Nadie sube directo a `main`.
- **Revisión:** cada PR lo aprueba el dueño de un módulo vecino.

| PR de | Revisa |
|---|---|
| Luca | Flor o Joaco |
| Flor | Luca (y Agus si toca el protocolo) |
| Ivan | Luca o Coro |
| Coro | Luca |
| Joaco | Flor o Luca |
| Agus | Flor o Ivan |

- **Terminado** quiere decir:
  - están las pruebas del contrato y las de aceptación del módulo;
  - `dotnet test server/Rts.slnx` da verde;
  - no hay warnings, porque la compilación los trata como errores.
- **Cambiar algo compartido** (un tipo de `Rts.Shared`, un DTO de `Rts.Protocol` o un contrato):
  - va en un PR que también actualiza el contrato y `docs/decisions.md`;
  - lo aprueban Luca y los dueños afectados.

  Nunca se cambia "de paso" dentro de otro PR.
- **No se edita el módulo de otro** sin hablarlo con su dueño.
- **Ante una duda** o una contradicción entre documentos: se pregunta al dueño del contrato y se registra la respuesta. No se resuelve en silencio (AGENTS §2).
- **Agentes de IA:** si se usan, que lean `AGENTS.md` y el contrato del módulo. El dueño responde por el código igual que si lo hubiera escrito.

## 6. Defensa

- Cada dueño explica sus módulos: qué hacen, cómo se probaron y qué decisiones tomaron.
- **Todos** deben poder explicar el núcleo concurrente, porque es lo que evalúa la materia:
  1. receptores concurrentes → cola global → consumidor único (Networking → Simulation);
  2. snapshot inmutable → Economy ∥ Combat → barrera → commit determinista;
  3. un escritor por cliente con cola acotada, para que un cliente lento no frene a nadie;
  4. GameSession como consumidor único de eventos de sesión, con un solo resultado oficial.
- Luca presenta la arquitectura general y el orden de construcción del Server (contrato 10 §4).
