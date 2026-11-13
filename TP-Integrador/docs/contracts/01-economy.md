# Contrato 01 — Economy (`Rts.Economy`)

> **Estado:** contratos, modelos, interfaces y configuración implementados (`server/src/Rts.Economy`) y probados (`server/tests/Rts.Economy.Tests`). **La lógica de cálculo todavía no está implementada.**
> **Etiquetas:** **DECIDIDO** = aprobado (módulo [01-economy](../modules/01-economy.md) o [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Depende de:** [Contrato 00](00-shared-kernel.md). No depende de ningún otro módulo.

## 1. Superficie pública

| Tipo | Namespace | Función |
|---|---|---|
| `IEconomyCalculator.CalculateTick(EconomyTickInput) → EconomyTickResult` | `Rts.Economy` | Cálculo del tick (fase 5, en paralelo con Combat) |
| `IEconomyRules` | `Rts.Economy` | Costos, saldo inicial, `EvaluateSpend`/`CanAfford` para gastos atómicos de Simulation y `EvaluateGatherTarget` para validar `GatherWood` en la fase 1 (RECOMENDADO D-092) |
| `EconomyTickInput`, `EconomyTickResult`, `PlayerResourceDelta`, `WoodGathered`, `FoodProduced`, `SpendEvaluation` | `Rts.Economy.Contracts` | Entradas y salidas inmutables |
| `GathererState`, `TreeEconomyState`, `FarmEconomyState`, `TreeStatus`, `FarmProductionStatus`, `GatherTargetInfo` | `Rts.Economy.Model` | Vistas económicas de entidades |
| `EconomyConfig` | `Rts.Economy.Configuration` | Parámetros tipados y validación |
| `EconomyErrors` | `Rts.Economy` | `Economy.InsufficientWood`, `Economy.InsufficientFood`, `Economy.InsufficientResources`, `Economy.InvalidGatherTarget`, `Economy.TreeDepleted` (D-092) |
| `TreeDepleted`, `TreeRespawned`, `FarmDepleted` | `Rts.Shared.Events` | Eventos propuestos (D-009) |

Son internos, y Simulation no los usa: `IGatheringCalculation` y `IFarmProductionCalculation`. Son los subcálculos que pueden correr en paralelo y que se consolidan en orden fijo (01 §7).

## 2. Propiedad de datos

| Dato | Dueño | Notas |
|---|---|---|
| Saldos de jugador | Simulation (`WorldState`) | Economy propone deltas; nunca muta saldos |
| Madera restante y estado del árbol | Economy calcula / Simulation confirma | Posición y celdas reservadas son del mapa |
| Contador de recolección | Estado de la orden del trabajador | Simulation lo reinicia al cambiar la orden |
| Tandas, temporizador y estado productivo de la granja | Economy calcula / Simulation confirma | |
| **Progreso de reparación de granja** | **Construction** (D-012) | Economy no lo ve; una granja en reparación sigue `Depleted` |
| HP, huella y estado de obra | Construction / Combat | Fuera de Economy |
| **Tabla de costos y saldo inicial** | **Economy** (D-015) | Construction y Production no leen costos |

## 3. Entrada (`EconomyTickInput`)

Simulation la construye desde el `TickSnapshot` posterior a Movement y Construction. El constructor exige IDs únicos y que cada recolector apunte a un árbol presente. El orden de las colecciones **no** afecta el resultado.

- `Gatherers`: solo los trabajadores **habilitados**, es decir, con `GatherWood` activa y **en rango** según Units / Movement. Simulation hace el filtrado; Economy no calcula distancias (RECOMENDADO D-016).
- `Trees`: todos los árboles, disponibles y agotados.
- `Farms`: granjas **completadas y existentes**; las obras en construcción quedan fuera.
- La configuración no viaja en la entrada: se inyecta al construir la implementación (RECOMENDADO D-017).

## 4. Reglas de cálculo que debe cumplir la implementación

Convención general: un "contador" acumula los ticks en que la condición se cumple en el snapshot. Al alcanzar el intervalo, la acción ocurre en ese tick y el contador vuelve a 0.

### Recolección (D-013)

1. Cada recolector suma 1 a su contador. La primera madera llega en el tick número `GatherIntervalTicks` dentro del rango, un intervalo completo después de llegar.
2. Al completar el intervalo, el trabajador solicita `WoodPerGather` y su contador vuelve a 0.
3. Por árbol, las solicitudes se atienden por **`WorkerId` ascendente**, cada una con `min(solicitado, restante)`. Se acepta recolección parcial; una asignación de 0 no genera registro (01 §3.5-6).
4. Si el árbol llega a 0, queda `Depleted` con `DepletedAtTick = Tick` y se propone `TreeDepleted`. **Todos** sus recolectores, hayan completado o no su intervalo, pasan a `WorkersToIdle` (01 §3.9).
5. Un recolector que apunta a un árbol `Depleted` en el snapshot pasa a `WorkersToIdle`. Es un caso defensivo; en la práctica Simulation no debería entregarlo.

### Regeneración de árboles (RECOMENDADO D-018)

6. Un árbol `Depleted` se regenera en el tick `DepletedAtTick + TreeRespawnTicks`: pasa a `Available(MaxWoodPerTree)` y se propone `TreeRespawned`.
7. En ese tick nadie recolecta de él, porque el snapshot lo muestra agotado. Puede recolectarse **desde el tick siguiente**, con una orden nueva (01 §3.10).

### Producción de granjas (01 §4, D-014)

8. Una granja `Producing` avanza su temporizador solo si `Tick >= ProductionActiveFromTick`.
9. Al alcanzar `FarmProductionIntervalTicks`, produce `FoodPerFarmBatch` (7) para su dueño, descuenta una tanda y su temporizador vuelve a 0.
10. Con 0 tandas pasa a `Depleted` y se propone `FarmDepleted`. No se destruye ni pierde HP.
11. Una granja `Depleted` no cambia en Economy.

### Consolidación y orden

12. `PlayerDeltas` suma `WoodGathered` y `FoodProduced` por jugador. Contiene solo deltas no nulos, ordenados por `PlayerId`.
13. Todas las colecciones del resultado se ordenan por ID ascendente.
14. `ProposedEvents` lleva primero los eventos de recolección y árboles, ordenados por `TreeId`, y después los de granjas, ordenados por `FarmId`. El orden no depende de cuál subcálculo termina primero.

## 5. Lo que Simulation debe hacer con el resultado (commit)

Esto vale para el implementador de Simulation; Economy no lo hace.

- Acreditar `PlayerDeltas` **aunque** el trabajador o la granja hayan sido destruidos por Combat en el mismo tick (01 §3, §4; AGENTS §3).
- Aplicar `TreeUpdates`. Aplicar `FarmUpdates` solo si la granja sigue existiendo; si fue destruida, el crédito se conserva igual.
- Pasar a Idle los `WorkersToIdle` que sigan vivos y actualizar el contador de los `GathererUpdates`.
- Cuando se confirma `TreeDepleted`, pasar a Idle **a todos** los trabajadores con `GatherWood` sobre ese árbol, también a los que todavía caminan hacia él: Economy solo ve a los que están en rango (01 §3.9).
- Sellar `ProposedEvents` como `DomainEvent` y derivar `ResourcesChanged` (solo dueño) de los saldos confirmados.
- Al completarse una granja en la fase 3 del tick N: crearla `Producing` con `BatchesPerFarmCycle` tandas, temporizador 0 y `ProductionActiveFromTick = N+1`.
- Al completarse una reparación (Construction) en el tick N: pasarla a `Producing` con `BatchesPerFarmCycle` tandas, temporizador 0 y `ProductionActiveFromTick = N+1` (D-014).
- Al reemplazar o cancelar la orden de un trabajador: descartar su contador de recolección (01 §3.8).
- Confirmar los gastos con `IEconomyRules.EvaluateSpend` en el mismo commit lógico que la operación (`Build`, inicio de `RepairFarm`, inicio de entrenamiento).

### Validación del objetivo de `GatherWood` en la fase 1 (RECOMENDADO D-092)

04 §3 pide que Economy valide que el árbol sea recolectable. Simulation llama a `IEconomyRules.EvaluateGatherTarget(GatherTargetInfo?)` por cada trabajador, después de verificar su propiedad y antes de Movement:

| Caso | Resultado |
|---|---|
| Objetivo inexistente (`null`) | `Common.EntityNotFound` |
| Objetivo que no es un árbol (unidad o edificio) | `Economy.InvalidGatherTarget` |
| Árbol agotado | `Economy.TreeDepleted` |
| Árbol disponible | válido (`null`) |

El tipo del trabajador (`Common.IncompatibleEntity`) lo valida Movement con `UnitOrderCompatibility`, y la alcanzabilidad, `EvaluateUnitOrder`.

## 6. Configuración (`EconomyConfig`)

| Parámetro | Validación | Valor |
|---|---|---|
| `InitialBalance` | sin componentes negativas | PENDIENTE |
| `MaxWoodPerTree`, `WoodPerGather` | > 0 | PENDIENTE |
| `GatherIntervalTicks`, `TreeRespawnTicks`, `FarmProductionIntervalTicks` | > 0 (ticks) | PENDIENTE |
| `FoodPerFarmBatch` | > 0 | **7** (definición funcional) |
| `BatchesPerFarmCycle` | > 0 | **5** (definición funcional) |
| `BuildingWoodCosts` | exactamente Farm, Barracks, Tower y Fence; ≥ 0; sin MainCenter | PENDIENTE |
| `FarmRepairWoodCost` | ≥ 0 | PENDIENTE |
| `UnitFoodCosts` | Worker y Soldier; ≥ 0 | PENDIENTE |

El trabajo requerido para reparar una granja **no** está aquí: es de Construction (D-012).

## 7. Pruebas de aceptación para la etapa de implementación

Estas pruebas de 01 §9 se escriben al implementar `IEconomyCalculator`:

- dos o más trabajadores sobre un árbol, con reparto 5/5/2;
- contador reiniciado al cambiar la orden (lado Simulation);
- árbol agotado ⇒ Idle;
- respawn con el mismo ID y un tick de espera;
- cinco tandas de 7 y agotamiento sin destrucción;
- `ProductionActiveFromTick` respetado tras completar y tras reparar;
- resultado idéntico al permutar las colecciones de entrada y al forzar distinto orden de finalización de Gathering y FarmProduction.

Las pruebas de gasto único, recolección con muerte simultánea y granja productora destruida son de integración con Simulation.

## 8. Pendientes

- Valores de balance (§6).
- Política detallada de publicación de cambios económicos en el JSON (State Sync).
- Métricas de Economy.
