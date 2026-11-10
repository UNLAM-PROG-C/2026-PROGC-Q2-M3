# Contrato 04 — Units / Movement (`Rts.Movement`)

> **Estado:** contratos, modelos, interfaces y configuración implementados (`server/src/Rts.Movement`) y probados (`server/tests/Rts.Movement.Tests`). **La lógica de validación, búsqueda y avance todavía no está implementada.**
> **Etiquetas:** **DECIDIDO** = aprobado (módulo [04-units-movement](../modules/04-units-movement.md) o [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Depende de:** [Contrato 00](00-shared-kernel.md). No depende de ningún otro módulo.

## 1. Superficie pública

| Tipo | Namespace | Función |
|---|---|---|
| `IMovementCalculator.CalculateTick(MovementTickInput) → MovementTickResult` | `Rts.Movement` | Avance oficial del tick (fase 2) |
| `IMovementRules` | `Rts.Movement` | `ValidateRoute`, `CheckReachability`, `IsWithinInteractionRange`, `EvaluateUnitOrder`, `EvaluateRepathResponse`, rangos y velocidad por tick |
| `MovementTickInput`, `MovementTickResult`, `OrderEnded`, `RouteValidation`, `UnitOrderRequest`, `UnitOrderEvaluation` | `Rts.Movement.Contracts` | Entradas y salidas inmutables |
| `UnitMovementState`, `MovementOrder`, `MovementTarget`, `NavigationGrid`, `UnitOrderCompatibility`, `OrderEndReason` | `Rts.Movement.Model` | Modelos |
| `MovementConfig` | `Rts.Movement.Configuration` | Parámetros tipados y validación |
| `MovementErrors` | `Rts.Movement` | `InvalidPath`, `TooManyWaypoints`, `RouteDoesNotReachGoal`, `DestinationOutOfBounds`, `StaleOrder`, `Unreachable` |
| `MoveCommand`, `StopCommand`, `AttackCommand`, `GatherWoodCommand`, `BuildCommand`, `RepairFarmCommand`, `RepathResponseCommand`, `UnitRouteCandidate` | `Rts.Shared.Commands` | Payloads de comandos (D-009) |
| `UnitOrderKind` | `Rts.Shared.Entities` | Tipos de orden |
| `UnitOrderChanged`, `RepathRequested` | `Rts.Shared.Events` | Eventos propuestos |

## 2. Propiedad de datos y coordenadas

- **Coordenadas (DECIDIDO D-029):** son idénticas entre Godot y el servidor:
  - servidor X = Godot X; servidor Z = Godot Z; se ignora Godot Y;
  - 1 unidad de mundo = 1 metro de Godot;
  - el mapa ocupa X y Z positivos, con la esquina de la celda (0,0) en el origen de Godot.
- Posición oficial, orden activa, `OrderId` y ruta restante viven en `WorldState` (Simulation). Movement propone `UnitUpdates`.
- `OrderId` lo asigna Simulation al aceptar una orden. Un repath aceptado conserva el mismo `OrderId`.
- `NavigationGrid`: Simulation la construye desde el mapa oficial. Solo edificios y vallas bloquean, desde su colocación; árboles y unidades no. Las celdas fuera del mapa cuentan como bloqueadas.
- La persecución y la alcanzabilidad son de Movement. Combat solo decide ataque y daño (D-019).

## 3. Comandos y evaluación de órdenes (fase 1)

**DECIDIDO (D-026):** todo comando que mueve unidades trae **una ruta candidata por unidad** (`UnitRouteCandidate`), calculada por Godot con `NavigationAgent3D`. Los waypoints no incluyen la posición actual, y la ruta puede estar vacía si la unidad ya está en destino o en rango.

Para cada unidad del comando, Simulation:

1. verifica que la unidad sea del jugador (`Common.NotOwner`);
2. valida el objetivo con su módulo dueño: Combat para `Attack`, Economy para que el árbol sea recolectable, Construction para `Build` y `RepairFarm`;
3. llama a `IMovementRules.EvaluateUnitOrder`.

Esa evaluación verifica, en este orden:

| Verificación | Error |
|---|---|
| Orden compatible con el tipo de unidad (`UnitOrderCompatibility`, 04 §2) | `Common.IncompatibleEntity` |
| Destino del Move dentro del mapa | `Movement.DestinationOutOfBounds` |
| Cantidad de waypoints ≤ `MaxWaypointsPerRoute` | `Movement.TooManyWaypoints` |
| Ruta válida contra el **mapa actual** (§4), sin importar la `RouteMapRevision` del cliente | `Movement.InvalidPath` |
| Move: el final de la ruta (o la posición actual, si la ruta está vacía) a ≤ `ArrivalTolerance` del destino | `Movement.RouteDoesNotReachGoal` |
| Tarea: el final de la ruta en rango de interacción del objetivo | `Movement.RouteDoesNotReachGoal` |

- Si la unidad es aceptada, recibe un `OrderId` nuevo, la orden y la ruta validada, y su repath pendiente se limpia.
- Si es rechazada, **conserva su orden anterior** (04 §2). En un comando grupal el resultado se informa por unidad.
- `Stop` deja la unidad Idle sin revertir progreso ni recursos.
- Para `Build` en la forma "colocar", cada trabajador se evalúa contra `map.WithBlockedFootprint(huella)`, y Construction exige al menos uno aceptado (03 §2, §5). `CheckReachability` sobre ese mismo mapa sirve para informar `Movement.Unreachable` en lugar de `Movement.InvalidPath` (ver [contrato 03 §3](03-construction.md#3-build-en-la-fase-1)).

**`RepathResponse` (DECIDIDO D-028):** entra por la cola global con `Sequence`, es idempotente por `MessageId`, y cuenta para `MaxCommandsPerTick` y para el límite por cliente. `EvaluateRepathResponse` exige que `OrderId` sea la orden vigente (si no, `Movement.StaleOrder`) y que la ruta sea válida y llegue a la misma intención. Si se acepta, se instala la ruta y se limpia `RepathPendingSinceTick`. Una respuesta tardía nunca revive una orden cancelada o sustituida (04 §8).

## 4. Validación de rutas (`ValidateRoute`)

La cadena de tramos es posición oficial → w1 → … → wn. Una ruta es válida si:

1. todos los waypoints son finitos y están dentro del mapa;
2. ningún tramo toca una celda bloqueada.

**"Toca"** (RECOMENDADO D-031) se entiende de forma conservadora, sin radio de unidad:

- Se consideran todas las celdas que el segmento atraviesa (*supercover*).
- Si el segmento pasa **exactamente por una esquina** de la grilla, cuentan las celdas a ambos lados. Eso prohíbe cortar esquinas bloqueadas en diagonal (04 §4).
- Un segmento que corre **exactamente sobre el borde** de una celda bloqueada también la toca.
- Consecuencia para Godot: hornear el `NavigationMesh` con un radio de agente mayor que 0, para que las rutas no rocen los obstáculos.

## 5. Avance del tick (`CalculateTick`, fase 2)

Distancia máxima por tick = `SpeedPerSecond / TickRate` (resuelve O-007). Cada unidad se procesa en orden de `UnitId`:

1. **Idle:** sin cambios.
2. **RepathPending:** pausada, sin moverse (04 §5). Si `Tick − RepathPendingSinceTick ≥ RepathTimeoutTicks`, la orden termina con `RepathTimeout` y la unidad queda Idle.
3. **Revalidación por mapa:** si `RouteMapRevision < Map.Revision` y quedan waypoints, se revalida la ruta restante desde la posición actual.
   - Si es inválida: `RepathPendingSinceTick = Tick` y se propone `RepathRequested(IsPaused: true)`.
   - Si es válida: se actualiza `RouteMapRevision`.

   Una colocación aceptada en la fase 1 se revalida en la fase 2 del mismo tick. Una destrucción solo libera celdas y no invalida rutas.
4. **Objetivo ausente** en una orden de tarea: termina con `TargetLost` (caso defensivo, D-025).
5. **Tarea en rango del objetivo:** la unidad se queda en el lugar y la tarea sigue activa; llegar al rango no la termina (04 §2).
6. **Persecución en Attack (DECIDIDO D-027),** si el objetivo es una unidad fuera de rango:
   - Si el segmento recto hasta la posición actual del objetivo es válido (§4), la ruta se reemplaza por ese único segmento y el soldado avanza.
   - Si no, sigue su ruta vigente. Si no le queda ruta o la ruta ya no termina en rango del objetivo, se pide ruta nueva con `RepathRequested(IsPaused: false)`, a lo sumo una vez cada `PursuitRepathIntervalTicks`.
   - Si `CheckReachability` indica que el objetivo es inalcanzable, la orden termina con `Unreachable` (04 §5).
7. **Avance:** recorre los waypoints restantes consumiendo el presupuesto de distancia del tick; el presupuesto atraviesa varios waypoints si alcanza (04 §3).
8. **Move:** al consumir el último waypoint, la orden termina con `Completed` y la unidad queda Idle.
9. **Tarea con la ruta agotada fuera de rango, objetivo estático:** termina con `Unreachable`. Es un caso defensivo, porque la ruta se validó para terminar en rango.
10. Por cada orden terminada se propone `UnitOrderChanged(UnitId, OrderId.None, Idle)`.

`CheckReachability` (DECIDIDO D-026) es una búsqueda en anchura (BFS) sobre celdas libres:

- 8 vecinos; una diagonal solo vale si sus dos celdas ortogonales también están libres;
- parte de la celda de la unidad;
- la meta es cualquier celda libre cuyo rectángulo esté a distancia ≤ rango del objetivo;
- no genera rutas para el cliente.

## 6. Lo que Simulation debe hacer

- Fase 1: evaluar órdenes y repaths como en §3, asignar `OrderId` nuevos, y aplicar las colocaciones (que cambian `MapRevision`) antes de la fase 2.
- Fase 2: confirmar `UnitUpdates` y `OrdersEnded` de inmediato, antes de Construction.
- Al destruir una entidad: cancelar las órdenes que la apuntaban (D-025).
- Antes de armar la entrada de Economy: filtrar los recolectores en rango con `IsWithinInteractionRange` (D-016).
- Sellar `ProposedEvents`. `RepathRequested` va **solo al dueño**; `UnitOrderChanged` va a ambos jugadores, salvo que State Sync decida otra cosa.

## 7. Configuración (`MovementConfig`)

| Parámetro | Validación | Valor |
|---|---|---|
| `SpeedPerSecond` (Worker, Soldier) | > 0, finito | PENDIENTE |
| `ArrivalTolerance`, `GatherRange`, `BuildRange` | > 0, finito | PENDIENTE |
| `AttackRange` | > 0; igual a `CombatConfig.Soldier.Range` (RECOMENDADO D-030, se valida en la composición) | PENDIENTE |
| `MaxWaypointsPerRoute`, `RepathTimeoutTicks`, `PursuitRepathIntervalTicks` | > 0 | PENDIENTE |

## 8. Pruebas de aceptación para la etapa de implementación

Estas pruebas de 04 §9 se escriben al implementar:

- compatibilidad de órdenes (ya cubierta por `UnitOrderCompatibility`);
- nueva orden válida que reemplaza a la anterior, e inválida que la conserva;
- grupo mixto aceptado en parte;
- ruta de varios waypoints que respeta la distancia por tick;
- rechazo de teletransporte y de exceso de velocidad;
- ruta bloqueada por edificio, por valla o por diagonal;
- `MapRevision` vieja revalidada;
- pared nueva ⇒ `RepathPending`;
- repath válido que reanuda; timeout que termina la tarea; `OrderId` viejo rechazado;
- rango medido respecto de la huella;
- persecución directa y con repath; objetivo destruido ⇒ Idle;
- BFS con camino cerrado por vallas;
- resultados idénticos al permutar las colecciones de entrada.

La selección cíclica de unidades superpuestas es de Godot.

## 9. Pendientes

- Valores (§7).
- Tolerancias numéricas de punto flotante en la validación de segmentos.
- Si una tarea en rango que se aleja sigue la ruta o persigue: solo aplica a Attack; los objetivos de las demás tareas son estáticos.
- Política de selección y UX en Godot.
- Búsqueda de la celda de spawn: se coordina con Production (O-010).
