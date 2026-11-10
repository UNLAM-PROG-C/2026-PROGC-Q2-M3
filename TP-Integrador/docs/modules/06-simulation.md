# 06 — Simulation

> **Estado:** arquitectura del loop y reglas de confirmación aprobadas conceptualmente; firmas y mecanismo concreto de commit por definir.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Economy](01-economy.md), [Combat](02-combat.md), [Construction](03-construction.md), [Units / Movement](04-units-movement.md), [Production](05-production.md), [GameSession](07-game-session.md), [Networking](08-networking.md), [State Sync](09-state-sync.md).

## 1. Propósito y autoridad — DECIDIDO

Coordinar el ciclo del juego y mantener el **único `WorldState` autoritativo** de la partida. **Simulation es el único escritor oficial** del estado de unidades, mapa, edificios, recursos, trabajos y resultado lógico. Los módulos de dominio calculan validaciones y propuestas con entradas explícitas; Simulation valida, combina y confirma en orden.

No envía directamente sockets, no construye JSON, no crea hilos por entidad, no reimplementa reglas internas de los demás módulos ni administra plazas/conexiones de jugadores.

## 2. Inicialización — DECIDIDO

- GameSession administra dos jugadores y solicita a Simulation crear el mundo inicial.
- Simulation inicializa mapa fijo/simétrico, reservas de árboles, Centros Principales, unidades y recursos iniciales **iguales para ambos** según configuración.
- Antes del comienzo de los ticks, State Sync genera `InitialState` y GameSession espera `ClientReady` de los dos jugadores.
- **No corren ticks de gameplay mientras la sesión esté en Starting.** En Running se aceptan órdenes de gameplay.
- Se gestiona **una partida por proceso servidor**.

## 3. Ciclo del tick — DECIDIDO

Frecuencia objetivo **20 Hz**, **50 ms entre ticks**, **sin ticks solapados**. Un tick que tarda más de 50 ms produce un retraso registrado, no una falla fatal automática.

| Fase | Responsabilidad | Escritura oficial |
|---|---|---|
| 1. Command batch | Tomar lote máximo configurable; validar y confirmar comandos en `Sequence` global | Simulation |
| 2. Movement | Calcular y confirmar nuevas posiciones/órdenes | Simulation |
| 3. Construction | Calcular y confirmar progresos de obra/reparación y finalizaciones | Simulation |
| 4. TickSnapshot | Congelar vista coherente e inmutable para módulos concurrentes | Ninguna |
| 5. Economy ∥ Combat | Ambos calculan sobre **el mismo** snapshot; subcálculos pueden paralelizarse | Ninguna |
| 6. Barrier + Commit | Esperar **ambos** resultados, validarlos y aplicar deltas de manera determinista | Simulation |
| 7. Victory Rules | Evaluar destrucción de bases; decidir victoria/empate | Simulation |
| 8. Production | Solo si la partida continúa: progreso, spawns, inicios y pago | Simulation |
| 9. State publication | Entregar eventos confirmados y vista estable a State Sync sin I/O TCP en tick | Ninguna de gameplay |

**Consecuencias aprobadas:**

- Una obra completada en tick N solo es **operativa N+1**; si luego sufre daño sin destruirse, continúa `Completed`.
- Un trabajador que estaba recolectando al crear snapshot puede obtener madera y morir en ese mismo tick.
- Una granja operativa puede producir comida y ser destruida en ese tick; la comida se acredita.
- Ataques simultáneos pueden producir muertes mutuas; torre destruida en el mismo tick puede disparar si era operativa en snapshot.
- Los recursos producidos por Economy en N pueden financiar entrenamientos durante Production de N.
- Un productor destruido en Combat de N no actúa en Production de N.
- Si ambas bases caen en el mismo tick, `Draw`; si termina la partida, no se ejecuta Production.

## 4. Entrada concurrente de comandos — DECIDIDO

- Networking cuenta con dos receptores concurrentes que entregan comandos a una **cola global thread-safe**.
- La admisión asigna `Sequence` único **coordinado con el orden real de encolado**; la hora de llegada no garantiza el mismo intercalado entre ejecuciones distintas.
- Al iniciar un tick, Simulation toma un **lote acotado**, con límite configurable, en orden creciente de Sequence. El resto **ya admitido** permanece en cola para ticks siguientes.
- Networking aplica además **límite de admisión por cliente**; esto no implica descartar comandos que ya ingresaron válidamente.
- Cada comando contiene `PlayerId` oficial de conexión y `ClientCommandId` del cliente; idempotencia por combinación `(PlayerId, ClientCommandId)`.
- Una solicitud duplicada, en cola o ya procesada, **no se ejecuta dos veces**. Si ya hay resultado, se reutiliza para responder al cliente.
- Se comunica `Accepted`, `PartiallyAccepted` o `Rejected`, y en comandos grupales resultado por unidad.
- En caso de rechazo de una orden individual, **se conserva su orden anterior**.

## 5. Atomicidad y validación — DECIDIDO

- Un comando que implica varias mutaciones debe confirmarse como **una sola operación lógica**. Ejemplo `Build`: comprobar huella/alcanzabilidad/recursos → reservar gasto, crear obra, ocupar mapa y actualizar órdenes aceptadas como un conjunto. Rechazo no genera gasto.
- Inicio de reparación: pagar una sola vez + iniciar estado de reparación. Inicio de entrenamiento: pagar una vez + crear trabajo activo.
- Economía y Combate **no** aplican deltas mientras falta resultado de la otra tarea. Barrera → validación conjunta → commit ordenado.
- Para ataques, Simulation agrega daño por objetivo, limita HP a cero y resuelve destrucciones una vez; para recursos, consolida deltas sin invalidar efectos del snapshot por muertes del mismo tick.
- La exacta estrategia de preparación/commit seguro sin medias mutaciones es **PENDIENTE de implementación formal**; no asumir que una sucesión de escrituras con posibilidad de excepción es automáticamente transaccional.
- Si una fase esencial falla de forma irrecuperable, **no** continuar arbitrariamente con medio resultado de Economy o Combat. Los cambios ya confirmados en fases anteriores no implican una capacidad de rollback total del tick.

## 6. Concurrencia y determinismo — DECIDIDO

- Economy y Combat trabajan en tareas simultáneas sobre una **entrada inmutable común**.
- Dentro de Economy, Gathering y Farm pueden ser subcálculos concurrentes; dentro de Combat, soldados y torres pueden serlo.
- Solo Simulation modifica `WorldState` al finalizar las barreras, por lo que **no se requieren mutexes por entidad** para sus cálculos puros.
- El orden de agregación de propuestas y resolución de disputas es **determinista** (por IDs, secuencias o prioridad de fase documentadas). Nunca depende del timing de finalización de threads.
- Reproducibilidad significa **mismas entradas y mismo orden oficial de comandos ⇒ mismo estado resultante**, no «el sistema operativo asigna siempre el mismo orden de recepción».
- No se ejecuta I/O TCP dentro del tick.

## 7. Errores — DECIDIDO

| Clase | Ejemplo | Consecuencia |
|---|---|---|
| Rechazo de negocio | Sin madera, objetivo inválido, comando incompatible | Rechazar y continuar |
| Error de protocolo/entrada recuperable | JSON malformado, ruta inválida | Rechazar mensaje/orden y continuar según política de Networking |
| Retraso de tick | Un tick tarda 65 ms | Registrar demora, **no** terminar automáticamente |
| Falla crítica de cálculo/commit | Resultado esencial inconsistente, módulo que no puede concluir de forma segura | Supervisar, intentar finalizar sesión de forma controlada |
| Tarea esencial bloqueada | Posible deadlock o espera indefinida | Timeout de seguridad configurable y supervisión fuera de la tarea; si no coopera, puede requerir terminación del proceso |

**La falla crítica es una imposibilidad de garantizar la consistencia**, no «cualquier excepción» ni «cualquier operación inválida». GameSession coordina el cierre; una desconexión la maneja ese módulo, no Simulation directamente.

## 8. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| DTO / operación | Función |
|---|---|
| `SimulationConfig` | TickRate, MaxCommandsPerTick, límites/diagnóstico y políticas de supervisión |
| `CommandEnvelope` | Sequence, PlayerId, ClientCommandId, CommandType, payload tipado |
| `CommandResult` | ID, tick, `Accepted/PartiallyAccepted/Rejected`, resultados por entidad, error tipado |
| `TickSnapshot` | Vista inmutable del mundo después de Movement/Construction |
| `TickResult` | Cambios confirmados, DomainEvents ordenados, posiciones y métricas |
| `InitializeMatch` | Crear WorldState inicial para la sesión |
| `ProcessCommandBatch` | Consumir/validar ordenadamente el lote |
| `AdvanceTick` | Orquestar una iteración sin solapamiento |
| `CreateTickSnapshot` | Construir vista consistente de entrada |
| `CommitResults` | Confirmar resultados validados |
| `EvaluateVictory` | Resolver destrucción de bases y empate |
| `PublishChanges` | Entregar resultados confirmados a State Sync sin red directa |

`DomainEvent`: evento confirmado con ID, TickId, tipo, entidad/payload y orden estable dentro de tick. La especificación C# concreta, así como la taxonomía exhaustiva de `ErrorCode`, quedan pendientes.

## 9. Eventos y publicaciones — DECIDIDO

- Los cambios se convierten en eventos **solo después del commit**.
- State Sync prepara **lotes incrementales por tick** para clientes y posiciones periódicas a **10 Hz**.
- Mensajes privados de saldo/producción solo al jugador propietario.
- El orden por destinatario debe preservar `InitialState` → `MatchStarted` → eventos de gameplay → `MatchEnded`, además de `UnitSpawned` antes de posiciones de esa unidad.
- Un envío TCP bloqueado **no** detiene el tick; los destinatarios lentos se controlan por colas de salida limitadas y política de desconexión.

## 10. Diagnóstico y pruebas — DECIDIDO / RECOMENDADO

**Diagnóstico configurable aprobado:** tick, número de comandos, aceptados/rechazados, tiempos por fase, espera de barrera, eventos confirmados, retrasos, fallas y motivo de fin. En ejecución normal usar registros resumidos y errores; modo debug habilita detalle por tick.

**Pruebas obligatorias:** comandos simultáneos sobre misma celda; lote de 100 con límite 30 deja 70 pendientes; duplicados sin doble gasto; grupo parcialmente válido; fracaso de Build sin descuento; Economy/Combat invertidos en tiempo real pero idéntico resultado; trabajador recolecta y muere; granja produce y se destruye; ataques mutuos; ambas bases mueren → Draw; Combat falla antes de barrera → no commit parcial Economy/Combat; productor destruido no inicia; overrun no solapa; no doble commit; estado inicial solo en Starting; no gameplay después de terminal.

**RECOMENDADO:** registrar métricas para defensa del TP y probar estrés con comandos aleatorios reproducibles y tareas artificialmente demoradas.

## 11. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** Simulation depende de abstracciones de subsistemas puros; módulos de dominio no dependen de Simulation; GameSession supervisa su ciclo y State Sync consume resultados confirmados.
2. **Modelos:** `WorldState` encapsulado con modificadores internos al motor, entidades con IDs estables, `TickSnapshot` inmutable especializado para cada módulo; distinguir estado inicial, de sesión y de red.
3. **Interfaces/DTOs:** contratos de resultados inmutables con `TickId`, `SourceRevision`/versiones cuando aplique, códigos de error y estrategias de commit explícitas; tests con fakes antes de integrar módulos reales.
4. **JSON:** Simulation nunca serializa directamente; Command Processor traduce NetworkMessage→CommandEnvelope; State Sync traduce DomainEvents→DTOs, con orden por destinatario.
5. **Configuración:** parametrizar TickRate(20 aprobado), máximos de comandos, timeouts de seguridad, métricas/log; evitar mezclar segundos de reloj con duraciones de gameplay en ticks.
6. **Organización del trabajo:** construir primero loop mínimo y fakes de módulos; probar barrera+commit, luego conectar Economy/Combat y demás dominios; mantener una prueba end-to-end de dos clientes/órdenes/eventos desde el inicio.

## 12. Pendientes explícitos

**PENDIENTE:** esquema real de `WorldState`; interfaz de módulos; mecanismo de commit/validación; modelo de temporización/fallos y cancelación; secuencias de eventos/errores compartidas; límites numéricos; reparto de tareas. Estos detalles deben consensuarse antes de que seis agentes construyan implementaciones acopladas.
