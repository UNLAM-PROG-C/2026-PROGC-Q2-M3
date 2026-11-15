# Documentación de Desarrollo — Fases 1 y 2
**Módulos asignados a Iván:** `Rts.Movement` y `Rts.Construction`  
**Materia:** Programación Concurrente  
**Proyecto:** RTS 1v1 Autoritativo en C# / .NET 10  

---

## 1. Visión General y Rol en la Arquitectura

El servidor opera mediante un **loop de simulación autoritativo a 20 Hz** (1 tick cada 50 ms) controlado por el módulo `Simulation`. 
Nuestros dos módulos (`Rts.Movement` y `Rts.Construction`) son **completamente puros y sin estado (stateless)**:
- No mantienen listas de entidades vivas ni modifican colecciones mutables compartidas.
- En cada tick, `Simulation` les provee snapshots inmutables con datos del mundo (`MovementTickInput` y `ConstructionTickInput`).
- Cada módulo ejecuta la matemática de negocio/geometría y retorna resultados inmutables (`MovementTickResult` y `ConstructionTickResult`).
- Se cumple con el estándar de la cátedra: **sin números mágicos**, métodos de **máximo 15 líneas**, documentación XML en todo miembro público y arquitectura plana de orquestación paso a paso.

---

## 2. Módulo `Rts.Movement` (Contrato 04)

Este módulo gestiona la validación espacial de rutas propuestas por los clientes (Godot), la alcanzabilidad en la grilla y el avance físico continuo en el plano $X/Z$.

```
+---------------------------------------------------------------------------------+
|                                 Rts.Movement                                    |
+---------------------------------------------------------------------------------+
|  1. SegmentGridValidator  --> Geometría pura: recorte de segmentos (Slabs).   |
|  2. ReachabilitySearch    --> BFS sobre grilla: conectividad sin cortes diag.   |
|  3. MovementRules         --> Implementa IMovementRules (Fase 1: Comandos).     |
|  4. MovementCalculator    --> Implementa IMovementCalculator (Fase 2: Avance).  |
+---------------------------------------------------------------------------------+
```

### 2.1. `SegmentGridValidator.cs` (Geometría del Segmento)
- **Problema:** Determinar si un segmento continuo entre dos waypoints ($A \to B$) atraviesa, toca el borde o roza la esquina de una celda bloqueada (edificios/vallas).
- **Enfoque geométrico:** Algoritmo de *Slabs* / Liang-Barsky en 2D:
  - $P(t) = A + t \cdot (B - A)$ con $t \in [0, 1]$.
  - `GetBoundingCellRange`: Calcula la caja de celdas con margen de 1 celda vecina alrededor del segmento.
  - `SegmentTouchesCell` y `ClipAxis`: Acotan el intervalo $[minRatio, maxRatio]$ en los ejes $X$ y $Z$. Si ambos intervalos se solapan, el segmento intersecta el volumen cerrado de la celda.
  - **Corte temprano:** Al primer contacto con una celda bloqueada (`map.IsBlocked(cell)`), retorna `true` inmediatamente.

### 2.2. `ReachabilitySearch.cs` (BFS de Conectividad)
- **Problema:** Comprobar si existe *algún* camino físico posible entre la posición de una unidad y el objetivo (árbol, edificio o unidad enemiga), sin depender de los waypoints del cliente.
- **Implementación:**
  - Búsqueda en anchura (BFS) en 8 direcciones sobre celdas libres.
  - **Invariante de esquinas:** Una transición diagonal solo se permite si **ambas** celdas ortogonales adyacentes están libres (prohíbe colarse entre dos esquinas bloqueadas en diagonal).
  - La meta se cumple cuando cualquier celda libre evaluada cae dentro de la distancia de interacción requerida con respecto al punto o huella del objetivo (`IsCellWithinTargetRange`).

### 2.3. `MovementRules.cs` (`IMovementRules` — Fase 1 del Tick)
Orquestador de validación de órdenes (`EvaluateUnitOrder`). Diseñado de manera **plana (sin anidamiento profundo)** usando el operador `??`:
```csharp
ErrorCode? error =
    CheckCompatibility(request)
    ?? CheckOrderBoundsAndLimits(request)
    ?? ValidateRoute(request.Map, request.Unit.Position, request.Waypoints).Error
    ?? CheckGoalReached(request);
```
1. **`CheckCompatibility`:** Verifica según `UnitOrderCompatibility` (Trabajador: Move, Gather, Build, Repair, Stop; Soldado: Move, Attack, Stop).
2. **`CheckOrderBoundsAndLimits`:** Valida que el destino esté contenido en el mapa (`Grid.Contains`) y que los waypoints no superen `MaxWaypointsPerRoute`.
3. **`ValidateRoute`:** Itera la cadena de waypoints garantizando que todos sean finitos y no toquen celdas bloqueadas.
4. **`CheckGoalReached`:** Evalúa si el último waypoint (o la posición actual) termina a distancia $\le ArrivalTolerance$ (en `Move`) o dentro del rango de interacción del objetivo (en tareas).
5. **`EvaluateRepathResponse`:** Valida respuestas de replanificación solicitadas a Godot, asegurando que la orden no haya caducado (`StaleOrder`), la ruta sea válida y alcance la meta.

### 2.4. `MovementCalculator.cs` (`IMovementCalculator` — Fase 2 del Tick)
Calcula el avance oficial por unidad para cada tick ordenado por `UnitId` ascendente (determinismo absoluto).
- **Presupuesto de distancia:** $budget = \frac{SpeedPerSecond}{TickRate}$.
- **Avance a través de waypoints:** Si la distancia al waypoint actual es $\le budget$, la unidad lo alcanza, descuenta el recorrido y continúa consumiendo el saldo hacia el siguiente waypoint en el mismo tick.
- **Revalidación por cambio de mapa:** Si `RouteMapRevision < Map.Revision`, revalida la ruta restante contra el mapa actualizado. Si fue obstruida por una construcción reciente, la unidad entra en `RepathPending` y emite `RepathRequested(isPaused: true)`.
- **Timeout de Repath:** Si permanece pausada esperando ruta por $\ge RepathTimeoutTicks$, su orden finaliza con `OrderEndReason.RepathTimeout` y pasa a `Idle`.
- **Tareas activas:**
  - Objetivo destruido $\to$ `OrderEndReason.TargetLost`.
  - Unidad dentro del rango de interacción $\to$ Permanece en el lugar y mantiene la tarea activa (`HoldPositionInRange`).
- **Persecución en combate (`Attack`):**
  - Si la línea recta hacia la posición actual del enemigo está libre, reemplaza la ruta por ese segmento directo y avanza.
  - Si está bloqueada pero el objetivo es alcanzable por BFS, avanza por la ruta existente y solicita un repath no pausado (`isPaused: false`) respetando `PursuitRepathIntervalTicks`.
  - Si el BFS determina que el enemigo está encerrado, cancela con `OrderEndReason.Unreachable`.

---

## 3. Módulo `Rts.Construction` (Contrato 03)

Controla la validación de colocación de estructuras, continuación de obras, reparaciones de granjas y el cálculo de avance del trabajo acumulado por tick.

```
+---------------------------------------------------------------------------------+
|                               Rts.Construction                                  |
+---------------------------------------------------------------------------------+
|  1. ConstructionRules      --> Implementa IConstructionRules (Fase 1).          |
|  2. ConstructionCalculator --> Implementa IConstructionCalculator (Fase 3).    |
+---------------------------------------------------------------------------------+
```

### 3.1. `ConstructionRules.cs` (`IConstructionRules` — Fase 1 del Tick)
- **`GetInitialHp`:** Calcula el HP inicial con el que nace una obra:
  $$\text{InitialHp} = \operatorname{clamp}\Big(\lceil \text{MaxHp} \times \text{InitialHpFraction} \rceil, \; 1, \; \text{MaxHp} - 1\Big)$$
  Garantiza que ninguna obra nazca completada ni con vida menor a 1.
- **`EvaluateSite` (`BuildCommand.Place`):**
  Valida la viabilidad física del emplazamiento en este orden estricto:
  1. Tipo construible (el Centro Principal `MainCenter` no se construye).
  2. Huella (`Footprint`) completamente dentro del mapa.
  3. Celdas libres de otras estructuras (`OccupancyView.StructureCells`).
  4. Celdas libres de árboles o reservas de árboles agotados (`OccupancyView.TreeReservedCells`).
  5. Celdas libres de unidades de cualquier jugador presentes al momento del comando (`OccupancyView.UnitCells`).
- **`EvaluatePlacement`:**
  Combina la validación del sitio con los resultados de los trabajadores asignados:
  - Si ningún trabajador puede alcanzar la obra, se rechaza todo el comando con `ConstructionErrors.NoReachableWorker`.
  - Si al menos uno puede alcanzarla, se aprueba la colocación (`Accepted`) con aceptación parcial por entidad para los trabajadores.
- **`EvaluateContinueBuild` (`BuildCommand.Continue`):**
  Valida que el edificio exista (`EntityNotFound`), pertenezca al jugador solicitante (`NotOwner`) y no esté completado previamente (`AlreadyCompleted`).
- **`EvaluateRepairStart` (`RepairFarmCommand`):**
  Valida que sea una granja propia, completada y agotada (`FarmNotDepleted`).
  - Si no tiene reparación activa $\to$ `RepairStartKind.StartAndPay` (requiere cobro de recursos).
  - Si ya tiene reparación en curso $\to$ `RepairStartKind.Join` (los obreros se suman sin costo adicional).

### 3.2. `ConstructionCalculator.cs` (`IConstructionCalculator` — Fase 3 del Tick)
Calcula las contribuciones agregadas de los trabajadores en rango (no hay concurrencia ni locks por estructura; se agrupan en un paso determinista):
- **Obras (`ProcessBuildings`):**
  $$\text{NewHp} = \min\big(\text{MaxHp}, \; \text{CurrentHp} + \text{WorkersCount} \times \text{BuildHpPerWorkerTick}\big)$$
  - Si alcanza `MaxHp`: Marca `IsCompleted: true` y propone el evento `BuildingCompleted`.
  - Si continúa en obra: Propone `BuildingProgressChanged(id, newHp)`.
- **Reparación de Granjas (`ProcessRepairs`):**
  $$\text{NewWork} = \text{WorkDone} + \text{WorkersCount} \times \text{RepairWorkPerWorkerTick}$$
  - Si alcanza `RepairWorkRequired`: Marca `IsCompleted: true` y propone `FarmRepairCompleted`.
  - Si continúa en progreso: Propone `FarmRepairProgressChanged`.

---

## 4. Cobertura de Pruebas Unitarias

Todos los contratos y escenarios de borde están validados con tests automatizados sobre .NET 10 (`dotnet test`):

### `Rts.Movement.Tests` (50 pruebas en total)
1. **Fase 1 (`MovementPhase1Tests.cs`):**
   - Presupuesto de velocidad por tick según `SpeedPerSecond` y `TickRate`.
   - Detección de colisiones en líneas rectas, diagonales que cortan esquinas y bordes tangentes.
   - Rechazo de órdenes por incompatibilidad, destinos fuera de grilla, exceso de waypoints y metas inalcanzables.
   - Avance continuo atravesando múltiples waypoints en un mismo tick si el presupuesto alcanza.
   - Determinismo estricto ante la permutación del orden de entrada de unidades.
2. **Fase 2 (`MovementPhase2Tests.cs`):**
   - BFS de alcanzabilidad libre vs. encerrado por muros de vallas.
   - Bloqueo diagonal estricto en el BFS ante obstáculos ortogonales.
   - Respuestas de repath válidas, vencidas (`StaleOrder`) y con caminos truncados.
   - Detección de nuevos obstáculos en el mapa con transición a `RepathPending` y evento `RepathRequested(isPaused: true)`.
   - Revalidación exitosa de revisión de mapa sin interrupción si la ruta sigue despejada.
   - Cancelación por timeout de repath (`OrderEndReason.RepathTimeout`).
   - Pérdida de objetivo en tareas activas (`TargetLost`) y mantención de orden activa al llegar al rango.
   - Persecución móvil directa y pedidos de repath no pausados ante desvíos por obstáculos.

### `Rts.Construction.Tests` (25 pruebas en total)
1. **Modelos y Contratos (`ContractsTests.cs`):**
   - Inmutabilidad de `OccupancyView` al aplicar `WithStructure`.
   - Exclusión del Centro Principal como estructura en construcción.
   - Validación de unicidad de IDs de trabajadores y targets en `ConstructionTickInput`.
2. **Lógica de Negocio (`ConstructionLogicTests.cs`):**
   - Cálculo y acotamiento de HP inicial con fracciones.
   - Rechazo de colocación sobre estructuras, árboles, unidades y fuera de límites.
   - Aceptación parcial vs. rechazo total por falta de obreros alcanzables.
   - Validación de estados de continuar obra y ciclo de reparación (`StartAndPay` vs `Join`).
   - Suma lineal de aportes por obrero en obra y reparación con emisión de eventos de finalización.
   - Determinismo ante permutaciones de entrada.

---

## 5. Resumen de Archivos Entregados

| Módulo | Archivo | Responsabilidad |
|---|---|---|
| `Rts.Movement` | `SegmentGridValidator.cs` | Validación geométrica pura de colisión segmento-celda. |
| `Rts.Movement` | `ReachabilitySearch.cs` | Algoritmo BFS para evaluar si un objetivo es alcanzable en la grilla. |
| `Rts.Movement` | `MovementRules.cs` | Validación de órdenes, velocidades, rangos y repaths (Fase 1). |
| `Rts.Movement` | `MovementCalculator.cs` | Avance físico continuo, tareas, revalidación y persecución (Fase 2). |
| `Rts.Movement` | `MovementPhase1Tests.cs` | Pruebas de avance básico y validaciones iniciales. |
| `Rts.Movement` | `MovementPhase2Tests.cs` | Pruebas de BFS, revalidación, timeout y combate dinámico. |
| `Rts.Construction` | `ConstructionRules.cs` | Validación de sitios, huellas, colocación, continuación y reparaciones. |
| `Rts.Construction` | `ConstructionCalculator.cs` | Cálculo del progreso de construcción y reparación por tick. |
| `Rts.Construction` | `ConstructionLogicTests.cs` | Pruebas de colocación, aportes agregados y eventos de finalización. |

