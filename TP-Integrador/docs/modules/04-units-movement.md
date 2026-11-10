# 04 — Units / Movement

> **Estado:** jugabilidad y arquitectura de desplazamiento aprobadas; contratos geométricos/DTOs y balance pendientes.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Simulation](06-simulation.md), [Combat](02-combat.md), [Construction](03-construction.md), [Production](05-production.md), [State Sync](09-state-sync.md).

## 1. Propósito — DECIDIDO

Gestionar **tipos de unidades, órdenes, movimiento oficial, rutas candidatas, validación espacial, alcanzabilidad y rangos de interacción**. Godot representa un mundo 3D, pero la lógica de movimiento ocurre sobre un **plano continuo X/Z**. El servidor es autoritativo sobre posiciones, órdenes y velocidades.

Units / Movement propone cambios; **Simulation es el único escritor de `WorldState`**. No descuenta recursos, no aplica daño, no construye ni comunica sockets directamente.

## 2. Unidades y comandos — DECIDIDO

| Tipo | Órdenes permitidas |
|---|---|
| **Trabajador** | `Move`, `GatherWood`, `Build`, `RepairFarm`, `Stop` |
| **Soldado** | `Move`, `Attack`, `Stop` |

- Cada unidad mantiene **una única orden activa**. Una nueva orden válida sustituye a la anterior.
- **Una orden inválida no borra ni modifica la orden anterior** de la unidad afectada.
- `Stop` cancela la tarea activa sin revertir progreso o recursos ya confirmados: no devuelve madera, no deshace obra, no reinicia gastos de entrenamiento, etc.
- Al terminar de desplazarse con orden `Move`, la unidad pasa a `Idle`.
- En órdenes de tarea (`GatherWood`, `Build`, `RepairFarm`, `Attack`) la unidad se desplaza hasta el rango de interacción y **mantiene la tarea activa** mientras corresponda; Movement no la marca terminada solo por llegar al alcance.
- Soldados persiguen objetivos móviles para atacar; si el objetivo es destruido o resulta inalcanzable, quedan Idle.
- Trabajadores que terminan obra/reparación pasan a Idle; los que agotan el árbol quedan Idle y no retoman automáticamente al respawnear.
- Selección de grupo puede contener unidades de tipos distintos: cada unidad compatible y válida se acepta individualmente; las restantes conservan orden anterior. Se informa `PartiallyAccepted` cuando corresponda.
- Una orden grupal `Move` indica **un mismo destino** para las unidades aceptadas; no hay formaciones.

## 3. Coordenadas y colisiones — DECIDIDO

- Coordenadas oficiales continuas **X/Z** (altura/gráficos de Godot no son autoridad de movimiento lógico).
- El servidor mantiene la posición oficial y aplica velocidad máxima por tick: `SpeedPerSecond / 20` como distancia máxima en un tick a 20 Hz, respetando el presupuesto a lo largo de múltiples waypoints.
- **No existen colisiones entre unidades durante desplazamiento.** Pueden cruzarse y superponerse, incluso compartir posición temporalmente.
- Godot permite selección de unidades superpuestas ciclando entre ellas al repetir clic. También se admite selección por rectángulo.
- Edificios y vallas son obstáculos desde su colocación, aun incompletos. Árboles **no obstaculizan movimiento**, aunque reservan celdas para Construction.
- **Excepción consciente en Spawn:** una unidad recién entrenada solo aparece en una **celda libre de unidades**; luego puede superponerse normalmente al moverse. Esa validación se coordina con Production y Simulation.
- No hay niebla de guerra; las unidades de ambos jugadores se ven según la sincronización oficial.

## 4. Cálculo de rutas y autoridad — DECIDIDO

- **Godot** utiliza `NavigationAgent3D`/`NavigationMesh` para proponer una ruta candidata de waypoints hacia el destino/objetivo.
- **Servidor** mantiene un modelo simplificado de mapa/grilla con celdas bloqueadas y **valida toda ruta candidata**: waypoints, segmentos entre puntos, límites, paso por celdas bloqueadas y cruces diagonales de esquinas bloqueadas.
- El cliente no puede informar una posición nueva como si fuera oficial. Solo propone la **ruta** y su destino.
- La orden de ruta incluye `MapRevision` que usó Godot. Simulation/Movement verifica si la revisión sigue vigente; si es vieja, revalida la ruta contra el mapa actual, en vez de confiar ciegamente en ella.
- `OrderId` lo asigna **el servidor**. Una respuesta de repath con `OrderId` viejo o equivocado se rechaza sin reemplazar la orden actual.
- Las órdenes inválidas por tipo, propiedad, objetivo o ruta no alteran la orden anterior.
- Las distancias de llegada, rangos de interacción y velocidades son **configurables**. Para edificios se mide rango con respecto a la **huella** (borde alcanzable), no necesariamente al centro.

## 5. Rutas invalidadas y Repath — DECIDIDO

- Al aceptar construcción/destrucción de edificios/vallas, cambia `MapRevision` y puede quedar inválida una ruta existente.
- Simulation/Movement revalida rutas afectadas y pone solamente esas unidades en `RepathPending`.
- La unidad en `RepathPending` **se pausa**, sin detener los demás ticks ni a las demás unidades.
- A través de Networking/State Sync se solicita a Godot una nueva ruta candidata para la **misma intención vigente**.
- El cliente responde con candidato, revisión de mapa y `OrderId`; el servidor verifica ruta actual y orden vigente antes de reanudar.
- Si el cliente no entrega una ruta válida dentro del **timeout configurable**, se cancela esa tarea/orden según la política de Movement y queda Idle; no se detiene la partida.
- Si destino/objetivo cambia dinámicamente, Movement puede solicitar replantear la ruta, sin otorgar autoridad de posición a Godot.
- Si no hay posibilidad de llegar, se rechaza el intento o la tarea llega a Idle conforme a su estado y momento.

## 6. Órdenes de interacción — DECIDIDO

- `GatherWood`: acercarse a un árbol válido y, al estar en rango, ejecutar la tarea de Economy; si árbol se agota, Idle.
- `Build`: llegar a posición válida alrededor de la huella; Construction acumula progreso de trabajadores; pueden unirse y abandonar.
- `RepairFarm`: solo granja agotada; acercamiento y trabajo cooperativo; la reparación no cura HP.
- `Attack`: soldado se acerca y persigue a objetivo enemigo; Combat decide cuándo hay alcance/cooldown y daño.
- `Move`: desplazarse a destino válido, luego Idle.
- `Stop`: cancelar orden; conservación de recursos/progreso regulada por módulo dueño.
- Cambiar orden de recolección reinicia intervalo de Gathering; cambiar objetivo de ataque **no** reinicia el cooldown de Combat.

## 7. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| Operación / DTO | Función |
|---|---|
| `MoveCommandInput` | Jugador, unidad(es), destino, waypoints, MapRevision y datos de comando |
| `EvaluateMoveOrder` | Validar compatibilidad, propiedad, ruta y condiciones del mapa |
| `ValidateRoute` | Verificar segmentos, obstáculos, límites y revisión del mapa |
| `CheckReachability` | Comprobar si existe interacción alcanzable alrededor de huellas/objetivos |
| `IsWithinInteractionRange` | Calcular rango para unidad/árbol/edificio según geometría |
| `MovementTickInput` | Snapshot del estado de órdenes, posiciones, waypoints, velocidades y mapa |
| `CalculateTick` | Proponer nuevas posiciones, progreso de órdenes, Idle y `RepathPending` |
| `MovementTickResult` | Deltas oficiales propuestos, cambios de orden y solicitudes de nueva ruta |

`OrderId`, `MapRevision`, IDs de entidad y errores pertenecen a contratos compartidos que todavía deben fijarse formalmente.

## 8. Orden, concurrencia e invariantes — DECIDIDO

- Simulation procesa comandos y ejecuta Movement **antes** de Construction y del snapshot compartido por Economy/Combat.
- Una ruta admitida no otorga libertad de movimiento fuera del trayecto validado ni permite superar velocidad máxima oficial.
- Múltiples unidades pueden mover concurrentemente **en sentido lógico** dentro de un tick, pero el cálculo/commit no implica escritores concurrentes del `WorldState` ni locks por unidad.
- La validez de una ruta se basa en la `MapRevision` oficial y sus obstáculos actuales.
- En grupos, una unidad con ruta inválida conserva su orden, aunque otras del mismo comando cambien correctamente.
- Un `RepathResponse` tardío no puede revivir una orden ya cancelada o sustituida.
- Ninguna ruta puede atravesar paredes/edificios o cortar esquinas bloqueadas por la grilla de validación.
- No se impone separación entre unidades, excepto **la celda de aparición inicial** de Production.

## 9. Pruebas de aceptación

**DECIDIDO:** trabajador y soldado aceptan/rechazan tipos correctos; Stop; nueva orden válida reemplaza y orden inválida conserva; grupo mixto parcial; superposición al moverse; selección cíclica para solapadas en Godot; ruta con múltiples waypoints respeta distancia/tick; rechazo de teletransporte y exceso de velocidad; ruta bloqueada por edificio/valla o diagonal; `MapRevision` vieja revalidada; nueva pared lleva a `RepathPending`; replanificación válida reanuda; timeout termina tarea; `OrderId` viejo se rechaza; rango respecto a huella; objetivo móvil perseguido; objetivo destruido → Idle; muerte de unidad cancela trabajo; modificaciones de mapa publicadas para navegación.

**RECOMENDADO:** pruebas geométricas de precisión y de rayos atravesando esquinas, tests con rutas de varios tramos y cálculos repetibles independientemente de orden de iteración.

## 10. Configuración — DECIDIDO / PENDIENTE

**Configurables aprobados:** velocidades por tipo, tolerancia de llegada, rangos de interacción, timeout de repath, geometría/escala grilla/huellas y parámetros de ruta. **PENDIENTE:** valores exactos, sistema de conversión coordenadas Godot↔servidor, límites de waypoints y tolerancias numéricas.

## 11. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** componente puro de validación geométrica y avance oficial; integrar con Construction por huella/alcanzabilidad y Combat por persecución, sin que esos módulos controlen posiciones.
2. **Modelos:** `UnitState` con `PositionXZ`, `CurrentOrder`, `OrderId`, `CurrentWaypoint`, `Speed` y estado de repath; `GameMap` con celdas/huellas, reservas de árboles y `MapRevision`.
3. **Interfaces/DTOs:** estructuras inmutables para `RouteCandidate`, `OrderResult`, `RepathRequest/Response`; errores diferenciados `InvalidPath`, `StaleOrder`, `Unreachable`, `InvalidTarget`.
4. **JSON:** Godot envía destinos y waypoints, nunca posiciones oficiales; Estado/State Sync publica cambios de órdenes y posiciones oficiales (10 Hz). Acordar convención exacta X/Z antes de implementar ambos extremos.
5. **Configuración:** definir cell size, distancias y velocidad como unidades mundo/s, rangos, precisión y timeout; probar determinismo y tolerancia a floats.
6. **Organización del trabajo:** comenzar con validación server-side de segmentos y speed budget, después rutas/órdenes; integrar Godot NavigationMesh y repath con una vertical slice; dejar la UX de selección en Godot aislada del servidor.

## 12. Pendientes explícitos

**PENDIENTE:** contratos definitivos de coordenadas, huellas, MapRevision, waypoints y errores; parámetros numéricos; protocolo de repath y reglas visuales exactas de selección; decisiones de implementación no deben alterar la semántica aprobada.
