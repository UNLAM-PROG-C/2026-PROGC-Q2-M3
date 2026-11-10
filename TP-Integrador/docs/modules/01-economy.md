# 01 — Economy

> **Estado:** definición funcional cerrada; contratos C# concretos y balance final pendientes.
> **Etiquetas:** **DECIDIDO** = aprobado; **RECOMENDADO** = sugerencia no aprobada; **PENDIENTE** = punto a especificar.
> **Referencias:** [Simulation](06-simulation.md), [Construction](03-construction.md), [Production](05-production.md), [Units / Movement](04-units-movement.md), [Combat](02-combat.md).

## 1. Propósito y alcance — DECIDIDO

Administrar la lógica de **madera** y **comida**, recolección, agotamiento y regeneración de árboles, generación de comida en granjas, estado de agotamiento de granjas y costos asociados. Economía no modifica `WorldState` directamente: propone resultados/validaciones para que Simulation los confirme. No gestiona sockets, selección de unidades, animaciones ni tiempos reales del reloj de la máquina para las mecánicas; los intervalos de gameplay se expresan en ticks.

## 2. Modelo de recursos — DECIDIDO

- La **madera** sirve para edificios y reparaciones de capacidad de granja.
- La **comida** se usa para entrenar unidades.
- Los recursos se contabilizan con **enteros** y **no tienen capacidad máxima**.
- Los árboles se regeneran y las granjas se pueden reparar tras agotarse; por lo tanto, la economía puede renovarse indefinidamente.
- Cada jugador dispone de sus propios saldos; State Sync comunica el saldo **solo al propietario**.
- Los costos y parámetros de ritmo son configurables. Cifras como «trabajador 3 de comida» o «soldado 5 de comida» fueron **ejemplos**, no balance numérico final aprobado.

## 3. Árboles y recolección — DECIDIDO

1. Una orden de un trabajador para recolectar madera apunta a un árbol con ID estable.
2. El trabajador debe estar dentro del rango de interacción; el movimiento para alcanzar el árbol pertenece a Units / Movement.
3. Cada trabajador tiene un intervalo de recolección configurable, medido en ticks; al cumplirse propone una cantidad de madera.
4. **Varios trabajadores pueden recolectar simultáneamente el mismo árbol.** No se implementa exclusión por árbol.
5. Si la madera disponible no alcanza para todos, se distribuye secuencialmente con un orden **determinista por `WorkerId`**, aceptando recolección parcial.
6. Ejemplo ilustrativo: árbol con 12 de madera y tres trabajadores que solicitan 5 cada uno → aportes 5, 5, 2 según orden establecido.
7. El recurso recogido se acredita inmediatamente en el tick que corresponde, **no** al volver a un edificio de almacenamiento.
8. Si un trabajador cambia de orden, cancela la recolección y **reinicia su contador del intervalo**. No se conserva progreso para recolección futura.
9. Cuando el árbol se agota, queda **invisible/no recolectable**; los trabajadores asignados a ese árbol pasan a Idle y no vuelven automáticamente a recolectarlo al regenerarse.
10. El árbol conserva **el mismo ID y posición**. Tras un tiempo de respawn configurable, recupera su `MaxWood` y vuelve a ser visible; será recolectable **recién desde el tick siguiente** a la regeneración.
11. La celda del árbol está reservada permanentemente para colocación de edificios, incluso mientras está agotado. **Los árboles no bloquean movimiento de unidades**.

**Simultaneidad con Combat:** si el trabajador estaba habilitado para recolectar en el snapshot y muere durante ese mismo tick de Combat, la madera se acredita igualmente. No depende de qué tarea concurrente termine primero.

## 4. Granjas y producción de comida — DECIDIDO

- Una Granja se construye pagando madera mediante Construction y pasa a operar **a partir del tick siguiente** al de finalización.
- Cada granja completada y operativa genera **7 unidades de comida por intervalo de producción** (intervalo configurable), de forma independiente de otras granjas.
- Cada granja admite **cinco tandas** antes de agotarse: capacidad de producción equivalente a 35 unidades de comida por ciclo completo.
- Una granja agotada queda `Depleted`/no operativa, **pero no destruida**: permanece en el mapa, ocupa su huella y puede sufrir daño de combate.
- La reparación de su **capacidad productiva** solo puede iniciarse después de agotarse; paga madera una sola vez al comenzar, requiere trabajo de trabajadores a lo largo del tiempo y admite contribuciones concurrentes acumulativas.
- Si los trabajadores abandonan la tarea, **el progreso de reparación se conserva**. Nuevos trabajadores pueden sumarse; no se vuelve a pagar.
- Reparación completada: restaura la posibilidad de producir otras cinco tandas y reinicia el temporizador de producción.
- **Esta reparación no recupera HP de combate.** No existe curación general de HP para edificios terminados.
- Si la granja es destruida durante la reparación, desaparece; el costo pagado y el progreso pendiente no se recuperan.
- Si una granja produce comida y es destruida en el **mismo tick**, **la comida se acredita y después se destruye**, dado que Economy y Combat consultaron el mismo snapshot.

## 5. Responsabilidades y límites — DECIDIDO

**Economy sí:** calcular recolección, regeneración y producción de granjas; evaluar saldos/costos; proponer agotamiento; proveer reglas económicas para gastos coordinados.

**Economy no:** descontar por su cuenta ante un `Build`/`TrainUnit`; instanciar edificios o unidades; resolver movimiento, HP, ataques o victoria; publicar JSON. Los gastos los confirma Simulation atómicamente con la operación correspondiente:

- `Build` → costo de madera + creación de obra + ocupación.
- Inicio de `RepairFarm` → costo de madera + inicio de reparación.
- Inicio de entrenamiento → costo de comida + activación de `ProductionJob`.

## 6. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| Operación / DTO | Función |
|---|---|
| `EconomyTickInput` | Tick, trabajadores recolectando, árboles, granjas, temporizadores y configuración, en lectura inmutable |
| `CalculateTick(input)` | Devuelve propuestas de recolección, producción, agotamiento y regeneración |
| `EconomyTickResult` | Deltas por jugador; actualizaciones de árboles/granjas y contadores; eventos propuestos |
| `CanAfford` / `EvaluateSpend` | Evalúa recursos suficientes, sin mutar saldo |
| `GetCost` | Obtiene costos configurados por construcción, reparación o entrenamiento |
| `GatheringCalculation` y `FarmProductionCalculation` | Subcálculos independientes potencialmente concurrentes |

**No inferir** de estos nombres una interfaz concreta ni acoplar Economy con el protocolo JSON. Definir el esquema definitivo en contratos compartidos posteriores.

## 7. Concurrencia, orden y garantías — DECIDIDO

- Economy y Combat calculan **concurrentemente** sobre el mismo snapshot inmutable, creado tras Movement y Construction.
- Dentro de Economy, **Gathering y FarmProduction** son subcálculos independientes que pueden ejecutarse concurrentemente; las propuestas se consolidan en orden fijo.
- Ninguna tarea de Economy escribe `WorldState`; por eso no se exige un mutex por árbol.
- Los trabajadores compitiendo por un árbol se resuelven de forma determinista.
- La acreditación de Economy y el daño de Combat corresponden a **un mismo instante lógico**, aunque el commit se realice secuencialmente.
- Production, que se ejecuta después de ambos, puede gastar comida generada por granjas en **ese mismo tick**, siempre que la partida no haya terminado.
- Un árbol regenerado no se recolecta hasta el tick siguiente.

## 8. Casos límite e invariantes — DECIDIDO

- No se generan saldos negativos mediante gastos aprobados.
- No se recolecta más madera de la que tiene el árbol.
- No se produce comida desde granjas agotadas, destruidas o todavía no operativas.
- La muerte del trabajador durante el tick de cálculo no invalida su recolección del snapshot.
- La destrucción de una granja durante el tick no elimina una tanda ya válida en ese snapshot.
- Un árbol agotado sigue reservando sus celdas para Construction.
- Una granja agotada no pierde sus HP por el mero agotamiento.
- La reparación de capacidad productiva nunca cura HP.
- Una propuesta de Economy no se confirma dos veces para el mismo tick.

## 9. Pruebas de aceptación — DECIDIDO / RECOMENDADO

**Obligatorias según reglas:** dos o más trabajadores sobre un árbol; reparto parcial 5/5/2; contador reiniciado al cambiar orden; árbol agotado → Idle; respawn con mismo ID y un tick de espera; prohibición de construir sobre árbol agotado; cinco tandas de 7 en granja; agotamiento sin destrucción; reparación de capacidad con varios trabajadores sin pago duplicado; persistencia de progreso; reparación sin curar HP; granja destruida durante reparación; recolección y muerte simultáneas; granja productora y destruida simultáneamente; no doble commit.

**RECOMENDADO:** pruebas repetidas alterando el orden artificial de terminación de Gathering/Farm/Combat para verificar saldos y eventos idénticos.

## 10. Configuración — DECIDIDO y PENDIENTE

**Configurables aprobados:** madera máxima por árbol, madera por recolección, intervalo de recolección, tiempo de regeneración del árbol, intervalo de granja, costos de edificios y de reparación, trabajo requerido para reparar, reglas de costo de entrenamiento. **Valores finales PENDIENTES** salvo `7 comida/tanda` y `5 tandas/granja` usados en la definición funcional (si se desea balancearlos después, requiere una nueva decisión).

## 11. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** mantener un componente económico puro, dependiente de contratos de dominio; Simulation coordina gastos para evitar llamadas directas de Construction y Production hacia Economy.
2. **Modelos:** `ResourceBalance`, `TreeState` (ID, posición, WoodRemaining, respawn), `FarmEconomyState` (tandas, timer, repair progress, status). Separar datos económicos de HP de combate.
   > **Actualización (D-012, [decisiones](../decisions.md)):** el *repair progress* pertenece a Construction, no a `FarmEconomyState`. Ver [contrato 01](../contracts/01-economy.md).
3. **Interfaces/DTOs:** resultados inmutables con deltas por entidad y jugador; explicitación de `TickId`, IDs y razones de rechazo económico. No entregar referencias a `WorldState` mutable.
4. **JSON:** publicar saldos económicos solo al dueño; difundir visibilidad de árboles y estados públicos de granjas a ambos mediante State Sync. Economy no serializa JSON.
5. **Configuración:** centralizar parámetros económicos tipados, validarlos al inicio (no negativos, intervalos positivos) y distinguir valor predeterminado de regla aprobada.
6. **Organización del trabajo:** empezar por `CalculateTick` y casos de prueba puros; simular integración con Construction/Production mediante DTOs; integrar luego gastos atómicos en Simulation.

## 12. Pendientes explícitos

**PENDIENTE:** firmas C# exactas; clases compartidas; costos definitivos de madera/comida; tasas y duraciones; estados/errores tipados; política de publicación detallada de cambios económicos en DTO JSON; métricas. Ninguno se considera aprobado por aparecer en esta lista.
