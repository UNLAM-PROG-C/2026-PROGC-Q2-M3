# AGENTS.md — RTS 1v1 · Programación Concurrente

> **Propósito:** instrucciones de trabajo para integrantes y agentes de programación que participen en este repositorio.
> **Estado:** guía de colaboración derivada de las decisiones de diseño. Los nueve módulos ya tienen contrato (`docs/contracts/`) y estructura de código sin lógica (`server/`); esta guía no los reemplaza.
> **Idioma de la documentación:** español. Nombres técnicos, identificadores y símbolos de implementación pueden estar en inglés.

## 1. Producto y alcance aprobado

Juego RTS multijugador competitivo **1 contra 1**. Dos clientes **Godot (escena 3D; lógica de desplazamiento en plano X/Z)** se conectan a un servidor **C#/.NET** autoritativo mediante **TCP + JSON UTF-8, un mensaje por línea**. Un proceso servidor administra una sola partida. No hay IA, niebla de guerra, matchmaking, lobby complejo, espectadores, reconexión, persistencia en base de datos ni múltiples partidas por proceso.

Objetivo de victoria: destruir el **Centro Principal** rival. Si ambos Centros Principales son destruidos en el mismo tick de combate, el resultado es empate. Una desconexión de un solo jugador durante `Running` da la victoria al que permanece conectado; las desconexiones de ambos antes de fijarse un resultado pueden conducir a finalización técnica sin ganador, según la regla de lote de eventos de sesión.

El proyecto debe permitir **demostrar, medir y explicar** concurrencia, sincronización y consistencia: receptores concurrentes → cola global → consumidor único; cálculos concurrentes de Economy y Combat sobre snapshot inmutable → barrera → commit centralizado; un escritor de red por cliente.

## 2. Fuente de verdad y jerarquía documental

1. **`docs/modules/*.md` — secciones `DECIDIDO`:** reglas y comportamientos aprobados. No reinterpretarlos ni modificarlos unilateralmente.
2. **Contratos (`docs/contracts/*.md`) y registro de decisiones (`docs/decisions.md`):** precisan *cómo* materializar las decisiones sin contradecirlas. Dentro de ellos rige la misma etiqueta: una entrada `DECIDIDO` fue aprobada por el equipo; una `RECOMENDADO` es una elección de quien redactó el contrato, vigente como punto de partida pero pendiente de revisión.
3. **`RECOMENDADO`:** propuesta de diseño o implementación, no decisión. Es posible evaluar alternativas; no elevar propuestas a requisitos sin aprobación.
4. **`PENDIENTE`:** detalle todavía no decidido (p. ej., balance numérico, esquema exacto de DTO, umbrales). No inventar una decisión definitiva; documentar suposición temporal si es indispensable para una prueba aislada.

Si dos documentos entran en conflicto, **detener el cambio afectado y reportar la contradicción** con referencias a ambas secciones. No resolverla silenciosamente. Las reglas transversales viven en el núcleo compartido (`docs/contracts/00-shared-kernel.md`) y en `docs/decisions.md`; una decisión nueva se registra ahí con su estado antes de usarse.

### Índice de módulos

| Archivo | Dominio |
|---|---|
| `docs/modules/01-economy.md` | Madera, comida, árboles, granjas |
| `docs/modules/02-combat.md` | Ataques, objetivos, daño, destrucciones |
| `docs/modules/03-construction.md` | Colocación, construcción y reparación de granjas |
| `docs/modules/04-units-movement.md` | Órdenes, rutas, posiciones, alcanzabilidad |
| `docs/modules/05-production.md` | Colas, entrenamiento, cobro y aparición |
| `docs/modules/06-simulation.md` | Ticks, fases, commit, determinismo |
| `docs/modules/07-game-session.md` | Conexión de jugadores y ciclo de vida |
| `docs/modules/08-networking.md` | TCP, JSON, admisión y transporte |
| `docs/modules/09-state-sync.md` | Snapshot inicial, eventos y posiciones |

### Contratos y decisiones

| Archivo | Contenido |
|---|---|
| `docs/work-plan.md` | Reparto entre los seis integrantes, sección del cliente Godot, fases, pruebas de integración por responsable y forma de trabajo |
| `docs/protocol/examples/` | Un JSON por cada mensaje del protocolo, generado desde los DTOs reales y verificado por `dotnet test` (para el cliente Godot) |
| `docs/decisions.md` | Registro de decisiones técnicas (`D-xxx`) y asuntos abiertos (`O-xxx`) |
| `docs/contracts/00-shared-kernel.md` | Núcleo compartido `Rts.Shared`: estructura de la solución, IDs, geometría/grilla, comandos, eventos, errores |
| `docs/contracts/01-economy.md` | Economy: entrada/resultado del tick, reglas de cálculo, propiedad de datos, costos, responsabilidades de commit |
| `docs/contracts/02-combat.md` | Combat: entrada/resultado del tick, rangos y cooldowns, daño agregado, validación de `Attack`, responsabilidades de commit |
| `docs/contracts/03-construction.md` | Construction: Build (colocar/continuar) atómico, RepairFarm, progreso de obra y reparación, ocupación, responsabilidades de commit |
| `docs/contracts/04-units-movement.md` | Units / Movement: comandos con rutas por unidad, validación de rutas, BFS de alcanzabilidad, persecución, repath, coordenadas Godot↔servidor |
| `docs/contracts/05-production.md` | Production: TrainUnit, colas FIFO, prioridad estricta por Sequence, progreso desde N+1, búsqueda de celda de aparición |
| `docs/contracts/06-simulation.md` | Simulation: WorldState inmutable, tick paso a paso, idempotencia, orden de eventos, loop, cola global, mapa e inicialización |
| `docs/contracts/07-game-session.md` | GameSession: máquina de estados pura y tabla de transiciones, plazas tras `Hello`, ReadyTimeout, lote de desconexiones tras detener ticks, host y salida del proceso |
| `docs/contracts/08-networking.md` | Networking: sobre y catálogo de mensajes, ciclo de vida de conexión y handshake, framing y parser, admisión con `CommandRejected`, faltas y abuso, cola de salida con saturación, heartbeat, hilos |
| `docs/contracts/09-state-sync.md` | State Sync: proyección sincrónica, visibilidad por evento, InitialState con catálogo, lotes por tick, posiciones cada N ticks y reemplazables, DTOs en `Rts.Protocol` y convenciones JSON |
| `docs/contracts/10-server.md` | Server (no es módulo): composición manual, orden de construcción "buzones primero", configuración y validación cruzada, catálogo, apagado ordenado y códigos de salida |

El código del servidor vive en `server/` (solución `server/Rts.slnx`; `dotnet test server/Rts.slnx`).

## 3. Invariantes arquitectónicos obligatorios

- **Un único `WorldState` autoritativo.** Solo `Simulation` escribe o confirma mutaciones sobre él. Ningún módulo de dominio ni componente de red modifica entidades oficiales por su cuenta.
- **Un solo loop de Simulation, 20 Hz objetivo, sin ticks solapados.** Cada tick tiene fases ordenadas; no se crean threads por unidad ni por árbol.
- **Comandos:** lectores concurrentes de ambos sockets; admisión segura que asigna `Sequence` global en el mismo orden de encolado; máximo configurable por tick; excedentes *ya admitidos* quedan pendientes. `PlayerId` sale de la conexión, nunca de un campo confiado al cliente.
- **Idempotencia:** clave `PlayerId + ClientCommandId`. Duplicados no producen segundas acciones, gastos o unidades; responder con el resultado original cuando corresponda.
- **Snapshot inmutable:** después de Movement y Construction, Economy y Combat calculan concurrentemente sobre el **mismo** estado congelado. Ninguno escribe en `WorldState`. La barrera reúne ambos resultados antes de aplicar el commit determinista.
- **Simultaneidad lógica:** un trabajador puede obtener madera y morir en el mismo tick; una granja puede generar comida y ser destruida en ese mismo tick. Ataques simultáneos pueden causar bajas mutuas; torre destruida en el tick puede disparar si era operativa en el snapshot.
- **Construcción:** edificios ocupan espacio desde su colocación; completarlos en el tick N los hace operativos recién en N+1, si sobreviven. Los árboles conservan celdas reservadas aunque estén agotados.
- **Movement:** servidor mantiene posiciones X/Z oficiales y valida rutas candidatas de Godot. La superposición entre unidades se permite *durante el movimiento*. **Spawn:** al crear una unidad, la celda debe estar libre de otras unidades; búsqueda determinista en radio configurable.
- **Production:** una tarea activa por productor + cola FIFO pendiente; gasto de comida al iniciar, no al solicitar; sin recursos se espera; `PendingSpawn` no bloquea Simulation y no cobra nuevamente; siguiente tarea como mínimo en el siguiente tick de Production.
- **Networking:** TCP/JSON UTF-8 por líneas; lectura asíncrona independiente y cola de salida limitada con escritor único por conexión. Ningún envío TCP bloquea Simulation.
- **State Sync:** snapshot inicial por destinatario, eventos incrementales ordenados por tick, posiciones oficiales a 10 Hz con interpolación en Godot; privacidad de recursos y colas de producción por jugador. Eventos estructurales no se descartan.
- **Sesión:** partida comienza únicamente después de dos conexiones y sus `Ready` válidos; un tercer cliente se rechaza; no hay reconexión.

## 4. Límites de dependencia y ownership

**Flujo lógico:** `Networking → Command Admission / GameSession → Simulation → módulos de dominio → resultados propuestos → Simulation → State Sync → Networking`.

- Los módulos `Economy`, `Combat`, `Construction`, `Units / Movement` y `Production` reciben **entradas explícitas de solo lectura** y devuelven **propuestas/resultados tipados**. No conocen TCP ni JSON ni orquestan otros módulos.
- `Simulation` integra validaciones, controla `WorldState`, resuelve orden/atomicidad y emite eventos confirmados. No debe duplicar algoritmos de negocio de otros módulos.
- `GameSession` escribe únicamente su `GameSessionState`; procesa eventos de sesión serializados, decide inicio/cierre y recibe el resultado de Simulation.
- `Networking` administra sockets, delimitación, versiones, lectura, escrituras, heartbeat, admisión y routing estructural. No aplica reglas de gameplay.
- `State Sync` transforma entradas inmutables confirmadas en DTOs de publicación; no mantiene un segundo mundo mutable.
- Los **modelos de dominio**, **interfaces entre módulos** y **DTOs JSON** no son intercambiables. Los modelos viven en cada módulo, los tipos de integración en `Rts.Shared` y los DTOs JSON solo en `Rts.Protocol` (D-049, D-066). Las referencias entre proyectos hacen cumplir estos límites (ver `docs/contracts/00-shared-kernel.md` §1).

## 5. Forma de trabajo de agentes e integrantes

1. Leer `AGENTS.md` y **el Markdown del módulo asignado**; leer los documentos de módulos vecinos que afecten la integración.
2. Extraer una lista de reglas `DECIDIDO`, propuestas `RECOMENDADO` y asuntos `PENDIENTE` aplicables a la tarea.
3. Antes de implementar, leer el contrato del módulo (`docs/contracts/`) y su sección "lo que deben hacer los demás". Si un caso no está cubierto o hace falta cambiar una interfaz compartida, proponerlo como cambio de contrato y registrarlo en `docs/decisions.md`; **no establecerlo privadamente**.
4. Respetar ownership: evitar editar modelos públicos o interfaces utilizadas por otros módulos sin acuerdo. Preferir cambios dentro del ámbito asignado.
5. Escribir pruebas unitarias e integración de invariantes, casos límite y concurrencia; utilizar mocks/fakes para dependencias aún no listas.
6. Para cada decisión nueva, etiquetar su estatus y registrar qué documento o contrato necesita actualización; un agente no puede declarar `DECIDIDO` una recomendación por su cuenta.
7. En una entrega, explicar cambios, pruebas ejecutadas, límites conocidos y cualquier desviación pendiente de aprobación.

**No iniciar implementaciones, generar assets de juego ni redefinir el alcance solamente por leer estos documentos**: cada tarea concreta deberá solicitarlo.

## 6. Disciplina de concurrencia y errores

- Nunca permitir modificaciones concurrentes de `WorldState` por «optimización».
- No encadenar locks cruzados de múltiples módulos ni ejecutar I/O de red dentro del tick.
- Un tick superior a 50 ms registra sobrecarga, **no** significa error fatal automático. Falla crítica es aquella que impide continuar conservando consistencia (resultados esenciales incompletos, imposibilidad de commit seguro, bloqueo sostenido supervisado).
- Rechazos de gameplay o JSON malformado **no** terminan la partida; errores técnicos críticos sí pueden requerir finalización. Desconexiones las resuelve GameSession.
- Los límites de cola son finitos. No confundir la admisión de Networking con el máximo de procesamiento por tick de Simulation.
- Toda salida hacia Godot debe preservar orden lógico por cliente: inicialización antes de `Ready`/inicio; `UnitSpawned` antes de posiciones de esa unidad; último evento de gameplay antes de `MatchEnded`.

## 7. Pruebas de integración prioritarias

- Dos jugadores generan comandos concurrentes: secuencias únicas, orden de encolado, sin duplicados.
- Colocación simultánea sobre una celda: una sola operación aceptada; costo deducido exactamente una vez.
- Economy y Combat calculan en distinto orden de finalización: resultado final idéntico para igual snapshot y entradas.
- Muertes simultáneas de ambos Centros Principales: empate.
- Recolección y muerte, o producción de granja y destrucción, en el mismo tick: recurso acreditado.
- Dos spawns: no seleccionan la misma celda libre en un mismo commit.
- Cliente lento: no bloquea Simulation ni escritor del otro cliente; se aplica la política de saturación.
- Desconexiones y `MatchEnded` concurrentes: un solo resultado oficial.
- Reproducción de eventos por cliente: mismo mundo público, información económica privada aislada.

## 8. Estado del proyecto y pendientes

**Formalizado** (contratos y estructura de código, sin lógica):

| Ámbito | Contrato | Decisiones |
|---|---|---|
| Estructura, tooling, IDs, geometría, errores | 00 | D-001 a D-011 |
| Economy, Combat, Construction, Units / Movement, Production | 01 a 05 | D-012 a D-042 |
| Simulation | 06 | D-043 a D-052 |
| GameSession | 07 | D-053 a D-061 |
| State Sync y esquema JSON (`Rts.Protocol`) | 09 | D-062 a D-072 |
| Networking | 08 | D-073 a D-080 |
| Server (composición, no es módulo) | 10 | D-081 a D-085 |

Todos los asuntos abiertos detectados en `docs/modules/` (O-001 a O-010) están resueltos. En `Rts.Server` ya funcionan la carga y validación de la configuración, el catálogo y el apagado ordenado (`rts-server --validate-only`).

**Implementado:** Simulation completó su parte de la Fase 1 (cola de comandos, cargador de mapa, motor con las fases 1, 2 y 9, loop de 20 Hz con supervisión y `SimulationFactory`); ver el estado del contrato 06 y las decisiones D-086 a D-091.

**PENDIENTE:**

- **Composición del servidor:** el `new` de cada pieza en `ServerComposition`, según el orden del contrato 10 §4, cuando existan las implementaciones.
- **Mapa:** el archivo de mapa real (formato D-052).
- **Valores numéricos:** balance (costos, HP, daño, rangos, velocidades, duraciones, tamaño del mapa), timeouts, capacidades de colas, límites de red y puerto.
- **Lógica:** implementar cada contrato y sus pruebas de comportamiento, incluidas las de integración de §7.
- **Revisión de las decisiones `RECOMENDADO`** de `docs/decisions.md`.
- **Cliente Godot:** copiar el esquema de `Rts.Protocol` y probar la ida y vuelta de mensajes.
- **Errores:** códigos que aparezcan al implementar, con el prefijo de área de su módulo (D-007).
- **Equipo:** el reparto está en `docs/work-plan.md`; la asignación de Flor, Ivan, Coro y Joaco es tentativa.

**RECOMENDADO:**

1. Implementar las piezas que instancia `Rts.Server`, empezando por las pasivas (cola de comandos, buzón y vista de sesión), con fakes donde haga falta.
2. Integrar una vertical slice: dos clientes conectados, un comando y un evento, con Networking y GameSession reales y una Simulation mínima.
3. Agregar después los módulos de dominio.

La división de tareas debe considerar dependencia real e integración continua, no asumir que cada uno de los nueve módulos equivale a un agente.

---

**Regla de oro:** ante cualquier tensión entre «hacerlo funcionar rápido» y «preservar una decisión aprobada», preservar la decisión y documentar el bloqueo. No inventar silenciosamente reglas nuevas.
