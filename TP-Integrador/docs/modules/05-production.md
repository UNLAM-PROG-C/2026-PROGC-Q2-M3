# 05 — Production

> **Estado:** mecánica aprobada y cerrada; contratos concretos y balance pendientes.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Economy](01-economy.md), [Construction](03-construction.md), [Units / Movement](04-units-movement.md), [Combat](02-combat.md), [Simulation](06-simulation.md).

## 1. Propósito y productores — DECIDIDO

Administrar solicitudes de entrenamiento y aparición de nuevas unidades. **Centro Principal** entrena **trabajadores** y **Cuartel** entrena **soldados**. Se admiten solicitudes únicamente para el tipo correcto y productor perteneciente al jugador. Los productores deben estar completados y **operativos**; un Cuartel finalizado en tick N puede comenzar a operar desde N+1. Production no modifica `WorldState`, no descuenta comida ni crea unidades por sí mismo; propone cambios para que Simulation los confirme.

## 2. Colas y solicitudes — DECIDIDO

- Cada edificio productor tiene **una tarea activa máxima** y **una cola FIFO de solicitudes pendientes**.
- El límite de cola es **configurable y se refiere a pendientes**, excluyendo la tarea activa.
- Una solicitud aceptada **no requiere comida en ese momento**; se encola incluso si el jugador carece de comida.
- No se permite cancelación manual de solicitudes ni de entrenamientos.
- Las solicitudes conservan un identificador y una secuencia global de admisión para determinar prioridad entre productores.
- Si el edificio es destruido, se pierden su trabajo activo y su cola; no se devuelve la comida ya gastada.
- Si la cola está llena, la solicitud se rechaza sin efectos secundarios.

## 3. Inicio y pago — DECIDIDO

- **La comida se descuenta una sola vez al iniciar el entrenamiento**, nunca al admitir la solicitud.
- Si no alcanza la comida, la solicitud permanece pendiente en `WaitingForResources` y no se descuenta nada.
- Cuando hay **varios productores compitiendo por comida escasa**, Simulation considera las solicitudes **elegibles** al frente de productores libres por su `Sequence` global, priorizando la elegible más antigua.
- El inicio y el gasto forman **un único commit lógico atómico**, para evitar entrenamiento sin pago o pago sin trabajo.
- Production corre **después de Economy y Combat** y, si la partida continúa, puede gastar comida producida en **ese mismo tick**.
- Un productor destruido durante Combat no inicia ni progresa trabajos de Production en ese tick.
- Los valores `3 comida por trabajador` / `5 comida por soldado` aparecieron como **ejemplos ilustrativos**. **PENDIENTE:** costo final y duración de cada unidad.

## 4. Entrenamiento y estados — DECIDIDO

Estados conceptuales: `Idle`, `WaitingForResources`, `Training`, `PendingSpawn`.

- `Idle`: no hay tarea activa; puede tener solicitudes pendientes.
- `WaitingForResources`: hay solicitud pendiente elegible, pero no comida suficiente para comenzar.
- `Training`: costo pagado y temporizador de entrenamiento avanzando por ticks según duración configurable del tipo de unidad.
- `PendingSpawn`: entrenamiento terminado, pero no hay celda válida de aparición; ocupa la única tarea activa del productor.
- **No se cobra de nuevo** por una tarea `PendingSpawn`; tampoco inicia otro entrenamiento mientras ocupa el productor.
- Si la unidad termina y aparece en el tick N, **la siguiente tarea solo puede comenzar en el próximo tick de Production (N+1 o después)**, no inmediatamente en la misma fase.
- Si un productor ya estaba libre al entrar a una fase de Production, una solicitud elegible puede comenzar en esa fase.
- **PENDIENTE:** convención exacta de si una tarea recién iniciada consume su primer tick de progreso de inmediato o a partir del siguiente tick; no cambiar la regla N+1 para siguiente tarea tras spawn.

## 5. Aparición (Spawn) — DECIDIDO

- El servidor busca posición cerca del edificio productor dentro de un **radio configurable y finito**.
- Elige la **primera celda válida siguiendo un orden fijo determinista**; no usa posiciones aleatorias.
- La celda de aparición **debe estar libre de otras unidades** en el momento de confirmar el spawn.
- También debe ser válida respecto de obstáculos/huellas del mapa y límites del mundo.
- **Excepción consciente:** Movement sí permite superposición y cruce de unidades mientras se desplazan; prohibirla al aparecer es una mejora visual local y **no** exige colisiones unitarias durante Movement.
- Si no existe posición disponible, la tarea pasa a `PendingSpawn`; reintenta en fases siguientes hasta encontrar posición o ser destruido su productor.
- Si dos edificios intentan generar unidades en un mismo tick, Simulation resuelve propuestas **en orden determinista y reserva/confirma la celda tras cada spawn**, para impedir que ambos aparezcan en la misma celda inicialmente libre.
- La unidad nueva obtiene un ID oficial del servidor y se comunica mediante `UnitSpawned` **antes** de posteriores actualizaciones de posición.

## 6. Destrucción y fin de partida — DECIDIDO

- El productor destruido por Combat desaparece, liberando mapa, y elimina trabajos pendientes/activos. La comida ya pagada **no se reembolsa**.
- Si Victory Rules termina la partida tras Combat, **no se ejecuta Production** de ese tick.
- No hay cancelaciones ni reembolsos por elección del jugador.

## 7. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| Contrato / operación | Función |
|---|---|
| `ProductionRequest` | `RequestId`, `Sequence`, `ProducerId`, `UnitType`, `PlayerId` |
| `ProductionJob` | Unidad, coste pagado, inicio, duración/progreso y estado |
| `ProductionTickInput` | Productores operativos, colas, trabajo activo, recursos disponibles, mapa/posiciones necesarias y configuración |
| `EvaluateTrainRequest` | Validar solicitud y capacidad de cola |
| `GetStartCandidates` / `EvaluateStartProduction` | Proponer inicios elegibles en orden global y evaluar pago |
| `CalculateTick` | Proponer progreso, finalizaciones y solicitudes de spawn |
| `EvaluateSpawn` | Consultar posición válida desde Units / Movement y proponer creación |
| `EvaluateProducerDestruction` | Proponer cancelación definitiva al destruirse productor |
| `ProductionTickResult` | Cambios propuestos de jobs, colas, spawns e inicios |

El gasto de comida lo confirma Simulation usando reglas económicas; la validación geométrica de spawn se coordina con Units / Movement. Production no modifica saldos ni agrega unidades al mundo directamente.

## 8. Concurrencia, orden e invariantes — DECIDIDO

- Solo Simulation decide inicio, descuento y spawn definitivo.
- Una solicitud con `ClientCommandId` duplicado no genera dos entradas ni dos cobros.
- Una sola tarea activa por productor y capacidad máxima de cola pendiente respetada.
- Inicio global determinista para productores elegibles ante comida escasa.
- Dos spawns no eligen la misma celda durante un mismo commit.
- Los resultados de Production no se aplican dos veces al mismo tick.
- `PendingSpawn` no bloquea el avance de Simulation ni cobra otra vez.
- El siguiente entrenamiento no comienza durante la fase en que el anterior finalmente apareció.
- Los nuevos productores solo operan al tick siguiente de completar obra.

## 9. Pruebas de aceptación

**DECIDIDO:** trabajador en Centro Principal aceptado; soldado en Cuartel aceptado; combinaciones erróneas rechazadas; cola llena rechazada; solicitud sin comida queda pendiente; dos productores competidores cobran al elegible más antiguo; inicio cobra exactamente una vez; tiempo de entrenamiento progresivo; spawn primera celda válida; primera celda ocupada se saltea; dos spawns simultáneos no comparten celda; radio completo bloqueado → `PendingSpawn`; liberar espacio permite aparecer sin cobrar otra vez; productor destruido pierde cola y tarea sin devolución; edificio recién completado espera N+1; trabajo siguiente solo inicia tick posterior a spawn; duplicado de solicitud no entrena dos veces; terminal por victoria evita Production.

**RECOMENDADO:** pruebas con secuencias de solicitudes de ambos jugadores y varios productores, incluyendo tareas bloqueadas por comida y por espacio.

## 10. Configuración — DECIDIDO / PENDIENTE

**Configurables aprobados:** costo en comida por tipo de unidad, duración de entrenamiento en ticks, capacidad de cola pendiente por productor, radio de búsqueda de spawn, reglas geométricas/tamaño de celda de spawn. **PENDIENTE:** valores concretos, orden exacto de recorrido de las celdas y convención de progreso de tarea recién iniciada.

## 11. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** mantener Production puro en términos de propuestas; Simulation coordina disponibilidad de comida (Economy) y posiciones válidas (Movement); evitar llamadas entre módulos de dominio.
2. **Modelos:** `ProducerState` con cola FIFO, trabajo activo y `OperationalFromTick`; `ProductionJob` con `StartedAtTick`, `RemainingTicks`, `PaidCost`, `PendingSpawn`; `ProductionRequest` con Sequence estable.
3. **Interfaces/DTOs:** separar validación de admisión, selección de candidato de inicio, cálculo de progreso, consulta de spawn y commit, con resultados explícitos `QueueFull`, `InsufficientResources`, `NoSpawnPosition`, etc.
4. **JSON:** `TrainUnit` incluye productor, tipo e ID de comando; `CommandResult` informa aceptación; `ProductionChanged` solo va al dueño; `UnitSpawned` se comunica a ambos. No exponer colas rivales.
5. **Configuración:** tabla de entrenamiento por unidad y radio/capacidad por productor; validar valores positivos y garantizar que radio/spawn usen la misma escala espacial que Movement.
6. **Organización del trabajo:** probar FIFO/pago con fakes de Economy, después tick de entrenamiento y `PendingSpawn` con un mapa de pruebas; integrar por último con Simulation y publicación.

## 12. Pendientes explícitos

**PENDIENTE:** firmas C# y DTO JSON exactos, costos/duraciones/capacidades, orden concreto de búsqueda espacial, tipos de rechazo y detalles de progreso en el tick de inicio. Ninguno de estos detalles habilita a un agente a cambiar las decisiones funcionales.
