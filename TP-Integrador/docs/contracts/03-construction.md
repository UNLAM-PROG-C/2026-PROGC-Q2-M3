# Contrato 03 — Construction (`Rts.Construction`)

> **Estado:** contratos, modelos, interfaces y configuración implementados (`server/src/Rts.Construction`) y probados (`server/tests/Rts.Construction.Tests`). **La lógica de validación y cálculo todavía no está implementada.**
> **Etiquetas:** **DECIDIDO** = aprobado (módulo [03-construction](../modules/03-construction.md) o [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Depende de:** [Contrato 00](00-shared-kernel.md). No depende de ningún otro módulo; usa resultados de Movement y Economy que Simulation le entrega como datos.

## 1. Superficie pública

| Tipo | Namespace | Función |
|---|---|---|
| `IConstructionCalculator.CalculateTick(ConstructionTickInput) → ConstructionTickResult` | `Rts.Construction` | Progreso de obras y reparaciones (fase 3) |
| `IConstructionRules` | `Rts.Construction` | Huellas, HP máximo e inicial, `EvaluateSite`, `EvaluatePlacement`, `EvaluateContinueBuild`, `EvaluateRepairStart` |
| `SiteEvaluation`, `PlacementInput`, `WorkerPlacementCandidate`, `PlacementEvaluation`, `RepairStartKind`, `RepairStartEvaluation` | `Rts.Construction.Contracts` | Colocación y reparación (fase 1) |
| `ConstructionTickInput`, `ConstructionTickResult`, `BuildingProgress`, `RepairProgress` | `Rts.Construction.Contracts` | Tick (fase 3) |
| `OccupancyView`, `BuildingUnderConstruction`, `FarmRepairState`, `WorkerContribution`, `ConstructionTargetInfo`, `RepairTargetInfo` | `Rts.Construction.Model` | Modelos |
| `ConstructionConfig` | `Rts.Construction.Configuration` | Parámetros tipados y validación |
| `ConstructionErrors` | `Rts.Construction` | `NotBuildable`, `OutOfBounds`, `CellOccupied`, `TreeReserved`, `UnitInFootprint`, `NoReachableWorker`, `AlreadyCompleted`, `NotAFarm`, `FarmNotDepleted` |
| `BuildCommand.Place` / `BuildCommand.Continue` | `Rts.Shared.Commands` | Dos formas del Build (D-032) |
| `BuildingPlaced`, `BuildingProgressChanged`, `BuildingCompleted`, `FarmRepairStarted`, `FarmRepairProgressChanged`, `FarmRepairCompleted`, `MapRevisionChanged` | `Rts.Shared.Events` | Eventos |

## 2. Propiedad de datos

| Dato | Dueño | Notas |
|---|---|---|
| Edificios, HP, estado de obra y ocupación | Simulation (`WorldState`) | Construction propone |
| HP máximo de edificios, huellas, trabajo de obra y de reparación | **Construction** (D-021) | `ConstructionConfig` |
| **Progreso de reparación de granja** (`FarmRepairState`) | **Construction** (D-012) | Existe solo mientras dura la reparación |
| Tandas, temporizador y `Depleted` de la granja | Economy | Construction lo recibe como `IsDepleted` |
| Costos de edificios y de reparación | Economy (D-015) | Simulation cobra con `EvaluateSpend` |
| Rango de construcción y reparación (`BuildRange`) y rutas | Movement | Simulation filtra los aportes en rango |

## 3. Build en la fase 1

### Forma "colocar" (`BuildCommand.Place`): un único commit lógico atómico (03 §2, 06 §5)

Simulation orquesta los pasos en este orden. Si uno falla, el comando se rechaza entero, sin gasto ni obra:

1. `EvaluateSite(type, anchor, occupancy)` verifica, en este orden:

   | Verificación | Error |
   |---|---|
   | Tipo construible (no el Centro Principal) | `Construction.NotBuildable` |
   | Huella dentro del mapa | `Construction.OutOfBounds` |
   | Huella sin otros edificios ni vallas, en obra o completados | `Construction.CellOccupied` |
   | Huella sin celdas reservadas de árboles, visibles o agotados | `Construction.TreeReserved` |
   | Huella sin unidades presentes de ningún jugador (celda que contiene su punto, D-004) | `Construction.UnitInFootprint` |

2. Simulation arma el mapa hipotético `NavigationGrid.WithBlockedFootprint(footprint)` y evalúa cada trabajador con `IMovementRules.EvaluateUnitOrder`. El objetivo es la huella y el rango es `BuildRange`. Puede usar `CheckReachability` para distinguir `Movement.Unreachable` de `Movement.InvalidPath`.
3. `EvaluatePlacement(PlacementInput)` repite la validación del sitio y combina los resultados por trabajador:
   - si **ninguno** es alcanzable, rechazo completo con `Construction.NoReachableWorker`;
   - si alguno lo es, aceptación parcial; los rechazados conservan su orden anterior (03 §2).
4. `IEconomyRules.EvaluateSpend(saldo, costo)`. Si no alcanza, rechazo completo con el error de Economy.
5. Commit, todo junto:
   - descontar la madera;
   - crear la obra con un `EntityId` nuevo y `GetInitialHp`;
   - ocupar la huella y avanzar la `MapRevision`;
   - dar a cada trabajador aceptado la orden Build sobre la obra, con su ruta y un `OrderId` nuevo;
   - emitir `BuildingPlaced`, `MapRevisionChanged` y `UnitOrderChanged`.

Simulation actualiza la `OccupancyView` (`WithStructure`) entre comandos del mismo lote. Así, dos colocaciones sobre la misma celda se resuelven por `Sequence`: se acepta una sola y la madera se descuenta una sola vez (AGENTS §7).

**Vallas (DECIDIDO D-033):** un `BuildCommand.Place` por celda. Cada uno es independiente y atómico: si la madera se acaba a mitad de una línea, se colocan las primeras. Networking debe permitir ráfagas razonables en su límite de admisión por cliente.

### Forma "continuar" (`BuildCommand.Continue`, DECIDIDO D-032)

1. `EvaluateContinueBuild(requester, target)`. Errores: `Common.EntityNotFound` (objetivo nulo), `Common.NotOwner`, `Construction.AlreadyCompleted`.
2. Cada trabajador se evalúa con Movement contra el mapa actual; el objetivo es la huella de la obra. No hay costo.
3. Los aceptados reciben la orden Build sobre la obra. El progreso previo se conserva (03 §3).

## 4. RepairFarm en la fase 1

`EvaluateRepairStart(requester, target)` verifica, en este orden:

| Verificación | Resultado |
|---|---|
| El objetivo existe | si no, `Common.EntityNotFound` |
| Es del jugador | si no, `Common.NotOwner` |
| Es una granja completada | si no, `Construction.NotAFarm` |
| Está agotada | si no, `Construction.FarmNotDepleted` (03 §4) |
| Ya hay una reparación en curso | `Join`: los trabajadores se suman sin pagar |
| No hay reparación en curso | `StartAndPay`: Simulation cobra el costo de Economy **una vez** y crea un `FarmRepairState(WorkDone = 0)` en el mismo commit; emite `FarmRepairStarted` |

Los trabajadores se evalúan con Movement, con la huella de la granja como objetivo y `BuildRange` como rango. Si ninguno es aceptado en un `StartAndPay`, el comando se rechaza entero, sin cobro (RECOMENDADO D-036, por analogía con Build).

## 5. Reglas del tick (fase 3) que debe cumplir la implementación

Los aportes son contribuciones agregadas, no escritores concurrentes (03 §7). Todas las colecciones se ordenan por ID.

1. **Obra:** `Hp' = min(MaxHp, Hp + cantidad_de_constructores_en_rango × BuildHpPerWorkerTick)`.
   - Una obra sin constructores no cambia.
   - Si `Hp' == MaxHp`, queda `IsCompleted` y se propone `BuildingCompleted`.
   - Si no, se propone `BuildingProgressChanged(id, Hp')`.
2. **Reparación:** `WorkDone' = WorkDone + cantidad_de_reparadores_en_rango × RepairWorkPerWorkerTick`.
   - Si `WorkDone' ≥ RepairWorkRequired`, queda `IsCompleted` y se propone `FarmRepairCompleted`.
   - Si no, se propone `FarmRepairProgressChanged`.
   - Sin reparadores no cambia: el progreso se conserva (03 §4).
3. **HP inicial (RECOMENDADO D-035):** `ceil(MaxHp × InitialHpFraction)`, con la fracción en (0, 1). Siempre queda en [1, MaxHp − 1], así que ninguna obra nace completa.

## 6. Lo que Simulation debe hacer con el resultado (commit de la fase 3)

- Aplicar el HP de cada `BuildingProgress`.
- Si la obra se completó en el tick N: marcarla `Completed` con `OperationalFromTick = N+1`. Si es una granja, crear su estado económico `Producing` con `ProductionActiveFromTick = N+1` (contrato 01 §5). Si es una torre, `ReadyAtTick = N+1` (contrato 02 §5).
- Pasar a Idle **a todos** los trabajadores con Build sobre esa obra, no solo a los que estaban en rango (03 §3).
- Si una reparación se completó en el tick N: borrar el `FarmRepairState`, pasar la granja a `Producing` con las tandas completas, temporizador 0 y `ProductionActiveFromTick = N+1` (D-014), y pasar a Idle a todos sus reparadores. No se toca el HP.
- Al destruirse una obra o una granja en reparación (Combat): borrar su `FarmRepairState` si lo tenía, sin reembolso; pasar sus trabajadores a Idle; liberar la huella; avanzar la `MapRevision`; emitir `EntityDestroyed` y `MapRevisionChanged` (D-025, D-034).
- Un edificio `Completed` dañado sigue `Completed`.

## 7. Configuración (`ConstructionConfig`)

| Parámetro | Validación | Valor |
|---|---|---|
| `BuildingMaxHp` (los 5 tipos) | ≥ 2 | PENDIENTE |
| `FootprintSizes` (los 5 tipos) | no vacía; **valla 1×1** | PENDIENTE |
| `InitialHpFraction` | en (0, 1) | PENDIENTE (el 10 % fue ilustrativo) |
| `BuildHpPerWorkerTick`, `RepairWorkRequired`, `RepairWorkPerWorkerTick` | > 0 | PENDIENTE |

## 8. Pruebas de aceptación para la etapa de implementación

Estas pruebas de 03 §9 se escriben al implementar:

- rechazos por límites, superposición, árbol agotado y unidad presente;
- grupo alcanzable en parte, y rechazo total sin trabajadores alcanzables;
- varios constructores que suman aportes;
- progreso conservado al abandonar;
- finalización con HP máximo;
- "continuar" sobre una obra ajena o completada;
- `RepairFarm` sobre una granja no agotada, `StartAndPay` frente a `Join`, y reparación interrumpida que conserva el progreso;
- resultados idénticos al permutar las entradas.

Son de integración con Simulation, Movement y Economy:

- gasto único y colocaciones simultáneas sobre una celda;
- muralla que cierra un camino y provoca repath;
- ocupación durante la obra;
- destrucción de una obra;
- finalización con operatividad en N+1;
- obra que pierde HP por ataque;
- edificio completado que no vuelve a obra;
- costo de reparación único.

## 9. Pendientes

- Valores (§7).
- Posiciones iniciales de los Centros Principales y diseño del mapa fijo y simétrico (Simulation).
- Cómo publica State Sync los eventos de progreso, por ejemplo si los coalesce (O-009).
