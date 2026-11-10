# 09 — State Sync

> **Estado:** estrategia funcional de sincronización aprobada; mensajes JSON exactos y política interna de buffers por formalizar.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Simulation](06-simulation.md), [Networking](08-networking.md), [GameSession](07-game-session.md), [Units / Movement](04-units-movement.md), [Economy](01-economy.md), [Production](05-production.md).

## 1. Propósito — DECIDIDO

Convertir estado y eventos **ya confirmados por Simulation** en mensajes para que dos Godot mantengan una representación coherente de un servidor autoritativo. State Sync no calcula reglas de negocio, no modifica `WorldState`, no mueve unidades y no escribe TCP directamente.

El mundo es **único**; la vista enviada puede diferir por jugador para proteger saldos y colas privadas. No existe niebla de guerra.

## 2. Tres mecanismos de sincronización — DECIDIDO

| Mecanismo | Momento | Alcance |
|---|---|---|
| **Initial snapshot** | Antes de Running, por cada `InitializationId` | Mapa, jugadores, unidades, edificios, árboles, estado inicial y datos propios |
| **Eventos incrementales** | Por tick confirmado, si hubo cambios publicables | Estado discreto de entidades, recursos, construcción, producción, destrucciones y mapa |
| **Posiciones oficiales** | **10 Hz** (cada **2** ticks de Simulation a 20 Hz) | Coordenadas X/Z oficiales de unidades relevantes para interpolación |

- No enviar snapshot completo continuamente ni implementar reconciliación periódica completa inicialmente.
- Eventos del mismo tick se **agrupan en un mensaje** por destinatario, respetando su orden. No se envían lotes vacíos necesariamente.
- Godot interpola **entre posiciones recibidas**, con pequeño retraso visual; **no extrapola** para adivinar movimientos futuros.
- Godot nunca reemplaza el estado oficial por decisiones locales de posición; no transforma paquetes de posiciones en autorización de movimiento.
- Posiciones pendientes pueden **consolidarse a la más reciente** cuando sea seguro, pero **eventos esenciales no se descartan**.

## 3. InitialState por destinatario — DECIDIDO

Durante `Starting`, después de registrar a P1 y P2, Simulation crea `WorldState` inicial y entrega una vista inmutable a State Sync. Se envía un DTO por cliente; ambos deben responder `ClientReady` con `InitializationId` válido antes de iniciar ticks.

| Campo conceptual `InitialStateDto` | Contenido |
|---|---|
| `SessionId` | Partida a la que pertenece |
| `InitializationId` | Inicialización validable por Ready |
| `PlayerId` | Identidad oficial del destinatario |
| `TickId` | Tick inicial |
| `Map`, `MapRevision` | Dimensiones, terreno y ocupación relevante |
| `Players` | Datos públicos de los dos jugadores |
| `Units` | IDs, propietarios, posiciones y atributos visibles |
| `Buildings` | IDs, propietarios, huellas, HP y estados |
| `Trees` | IDs, posiciones y estados visibles |
| `OwnResources` | Madera/comida **solo del destinatario** |
| `OwnProduction` | Trabajos/colas **solo del destinatario** |

Un `Ready` de inicialización cancelada no habilita Running. Si se reemplaza un jugador durante Starting, se invalida el estado Ready anterior y se prepara nueva inicialización.

## 4. Eventos incrementales — DECIDIDO

Los `DomainEvents` solo se crean/publican tras confirmar cambios en Simulation; State Sync no publica propuestas no aplicadas.

| Evento conceptual | Efecto en Godot | Visibilidad |
|---|---|---|
| `BuildingPlaced` | Instanciar obra y actualizar ocupación | Ambos |
| `BuildingProgressChanged` | Actualizar HP/progreso | Ambos |
| `BuildingCompleted` | Marcar edificio terminado | Ambos |
| `UnitSpawned` | Instanciar nueva unidad | Ambos |
| `EntityDamaged` / `UnitDamaged` | Actualizar HP visible | Ambos |
| `EntityDestroyed` | Eliminar entidad; liberar obstáculo si edificio | Ambos |
| `TreeDepleted`, `TreeRespawned` | Ocultar/mostrar árbol con mismo ID | Ambos |
| `FarmStateChanged` | Reflejar agotamiento/reparación | Ambos |
| `ResourcesChanged` | Actualizar HUD de recursos | **Solo propietario** |
| `ProductionChanged` | Actualizar trabajo activo/cola | **Solo propietario** |
| `CommandResult` | Aceptado, parcial o rechazado, con correlación | **Solo emisor** |
| `MatchStarted`, `MatchEnded` | Inicio/finalización oficial de sesión | Ambos, si conectados |

Los nombres pueden precisarse en contrato JSON. Los eventos de gameplay poseen **ID/orden estable y TickId**, definidos por Simulation. La información privada no se fuga en payloads públicos.

## 5. Posiciones e interpolación — DECIDIDO

- Simulation trabaja a **20 Hz**, State Sync publica posiciones a **10 Hz** de forma configurable (valor acordado inicial), aproximadamente cada 100 ms.
- `PositionUpdateDto` contiene `TickId` y pares `UnitId` + posición oficial **X/Z**. No necesita reenviar la entidad entera.
- Godot representa el desplazamiento mediante interpolación entre posiciones **ya recibidas**; puede verse levemente retrasado respecto de la simulación.
- Cada Godot **descarta posiciones viejas** según el último tick oficial de posición aplicado, sin retroceder visualmente.
- Si una entidad ya fue destruida, una actualización de posición tardía **no** la recrea.
- `UnitSpawned` debe preceder a la primera posición de una unidad recién creada.
- El servidor conserva autoridad sobre posiciones; no acepta posiciones propuestas directamente por Godot (Godot solo propone rutas candidatas para Movement).

## 6. Orden y consistencia por cliente — DECIDIDO

1. `InitialState` antes de `ClientReady`; `MatchStarted` antes de publicaciones de gameplay de Running.
2. Ticks publicados **en orden creciente**. Cada lote contiene eventos de ese tick en su orden oficial.
3. Evento de creación antes de movimientos/posiciones correspondientes; evento de destrucción no puede ir seguido de una publicación que implícitamente reviva esa entidad.
4. Los cambios de mapa se publican con `MapRevision` correcta para que Godot actualice navegación y responda repath cuando sea necesario.
5. Últimos cambios de gameplay confirmados **antes** de `MatchEnded`.
6. Networking usa **un escritor único por conexión**, pero ese hecho no resuelve por sí solo el orden de publicaciones generadas por varias tareas: se necesita una **secuencia lógica de publicación por destinatario**.
7. La sincronización no puede depender de que Godot vea accidentalmente un mensaje antes de otro debido a tiempos de ejecución.

## 7. Consolidación segura de posiciones — DECIDIDO

- Si hay varias actualizaciones de posiciones esperando **antes de entrar definitivamente a la cola de envío**, State Sync puede conservar la más reciente.
- La consolidación se realiza **sin atravesar barreras estructurales** que cambian el significado del flujo: spawn, destrucción, cambio relevante de mapa, resultado final.
- Nunca se eliminan silenciosamente eventos esenciales (`UnitSpawned`, `EntityDestroyed`, costos/saldos confirmados, etc.).
- No se intenta modificar bytes o mensajes que ya comenzaron a enviarse por TCP.
- Si un cliente sigue saturado y sus publicaciones esenciales no caben, Networking aplica su política de cola limitada y eventual desconexión; **Simulation no espera I/O**.
- La política exacta del buffer, tamaños y algoritmo de coalescing es **PENDIENTE**, siempre sujeta a esas garantías.

## 8. Navegación y mapa — DECIDIDO

- `BuildingPlaced` y `EntityDestroyed` (cuando afecta edificios/muros) alteran ocupación y `MapRevision`.
- `BuildingCompleted` **normalmente no** modifica ocupación, porque edificio ya bloqueaba desde colocación.
- `TreeDepleted`, `TreeRespawned` y `UnitSpawned` **no cambian** navegación: árboles y unidades no son obstáculos de movimiento.
- Godot actualiza su NavigationMesh/representación cuando recibe modificaciones de obstáculos.
- Simulation/Movement gestiona rutas invalidadas y `RepathPending`; State Sync transporta revisiones y solicitudes/respuestas vinculadas sin validar rutas.

## 9. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| Operación / DTO | Función |
|---|---|
| `BuildInitialState` / `InitialStateDto` | Snapshot estable de arranque, personalizado por PlayerId |
| `StateSyncTickInput` | Tick, DomainEvents, posiciones, MapRevision y estado de partida, inmutables |
| `BuildEventBatch` / `EventBatchDto` | Seleccionar eventos por destinatario y empaquetarlos por tick |
| `BuildPositionUpdate` / `PositionUpdateDto` | Posiciones oficiales a 10 Hz según TickId |
| `PublishTickResult` | Consumir resultado confirmado y preparar publicaciones |
| `StateSyncOutput` | Mensajes broadcast y por-jugador, sin efectuar I/O |
| `EnqueuePublication` | Publicar mensajes en orden lógico hacia Networking |
| `FlushPending` | Coordinar últimos mensajes durante cierre normal limitado |

Ningún DTO debe contener referencias a `WorldState` mutable. La selección privado/público es responsabilidad de State Sync, sobre datos ya confirmados.

## 10. Concurrencia, pruebas e invariantes — DECIDIDO

- Los resultados de Simulation entran en un **flujo de publicación ordenado**, no en múltiples escritores TCP paralelos.
- Un cliente lento no bloquea al otro ni Simulation; se aplica saturación de Networking cuando corresponda.
- No se publica un evento antes de su commit.
- P1 no recibe saldos/colas privados de P2 y viceversa.
- Las posiciones no crean entidades, no se aplican si son viejas y no adelantan su tick lógico.
- Eventos esenciales no se descartan, aun con coalescing de posiciones.

**Pruebas obligatorias:** ambos reciben mundo inicial equivalente salvo datos privados; Ready basado en InitializationId; evento BuildingPlaced y MapRevision para ambos; madera/comida solo dueño; UnitSpawned antes de posición; muerte elimina unidad y posición tardía no la revive; tras 20 ticks como máximo 10 actualizaciones periódicas de posiciones a 10 Hz; lote de eventos por tick ordenado; consolidar posiciones pendientes sin perder evento de spawn/destrucción; edificio colocado y destruido rebakea navegación; dos bases destruidas → ambos reciben empate; MatchEnded después de eventos finales; cliente lento no bloquea al otro; reconstrucción de representación pública coherente en consumidores simulados.

**RECOMENDADO:** replay de cientos de ticks sobre clientes de prueba sin Godot, verificando convergencia del estado público y aislamiento de datos privados.

## 11. Configuración — DECIDIDO / PENDIENTE

**DECIDIDO:** frecuencia de posiciones **10 Hz** frente a Simulation **20 Hz**; interpolación sin extrapolación; eventos por tick; consolidación segura; snapshot inicial sin reconciliación completa periódica. **PENDIENTE:** tamaño de mensajes/buffers, precisión numérica en coordenadas, política de lote/flush y frecuencia de métricas de red.

## 12. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** State Sync como adaptador/consumidor de resultados inmutables de Simulation, salida a una interfaz de publicación ordenada por cliente de Networking; GameSession coordina inicio y fin.
2. **Modelos:** `DomainEvent` con secuencia y TickId, `InitialStateDto`, `EventBatchDto`, `PositionUpdateDto`, metadatos mínimos por destino (`LastPublishedTick`, posición pendiente); **no** segundo WorldState.
3. **Interfaces/DTOs:** definir visibilidad (`AllPlayers`, `OwnerOnly`, `OriginatorOnly`) como metadato de eventos o criterio de proyección; incluir MapRevision y IDs estables.
4. **JSON:** establecer un esquema único común con Networking: `InitialState`, `StateEvents`, `PositionUpdate`, `CommandResult`, `MatchEnded`; pruebas de payload de privacidad y orden en Godot.
5. **Configuración:** mantener relación 20/10 inicial; medir bytes/mensajes por tick, profundidad de buffers y tiempo de interpolación; parametrizar sin cambiar autoridad del servidor.
6. **Organización del trabajo:** empezar con initial snapshot + `UnitSpawned`, luego eventos económicos/estructura, después posiciones 10 Hz/interpolación, y finalizar con coalescing y casos de red lenta.

> **Nota (2026-11-10):** los asuntos de este módulo se formalizaron en el [contrato 09](../contracts/09-state-sync.md). Quedaron DECIDIDOS: cadencia cada `TickRate / PositionRateHz` ticks (D-062), dueño en los eventos privados (D-063), proyección sincrónica con posiciones reemplazables (D-064), progreso y daño separados (D-065), proyecto `Rts.Protocol` (D-066), `UnitOrderChanged` para ambos (D-067) y catálogo en el InitialState (D-068). `MatchStarted` y `MatchEnded` los envía GameSession, y su orden queda garantizado por D-055 y D-064.

## 13. Pendientes explícitos

**PENDIENTE:** tipos JSON definitivos, estructura exacta de eventos/lotes y secuencia, contrato de publicación entre GameSession y State Sync, algoritmo de coalescing, precisión espacial y límites de buffer. No modificar la regla fundamental de publicación ordenada y autoritativa.
