# Contrato 02 — Combat (`Rts.Combat`)

> **Estado:** contratos, modelos, interfaces y configuración implementados (`server/src/Rts.Combat`) y probados (`server/tests/Rts.Combat.Tests`). **La lógica de cálculo todavía no está implementada.**
> **Etiquetas:** **DECIDIDO** = aprobado (módulo [02-combat](../modules/02-combat.md) o [decisiones](../decisions.md)); **RECOMENDADO** = elección pendiente de revisión; **PENDIENTE** = no definido.
> **Depende de:** [Contrato 00](00-shared-kernel.md). No depende de ningún otro módulo.

## 1. Superficie pública

| Tipo | Namespace | Función |
|---|---|---|
| `ICombatCalculator.CalculateTick(CombatTickInput) → CombatTickResult` | `Rts.Combat` | Cálculo del tick (fase 5, en paralelo con Economy) |
| `ICombatRules` | `Rts.Combat` | `ValidateAttackTarget` (fase 1, al procesar `Attack`) y `GetUnitMaxHp` (spawn) |
| `CombatTickInput`, `CombatTickResult`, `AttackPerformed`, `TargetDamage`, `CooldownUpdate` | `Rts.Combat.Contracts` | Entradas y salidas inmutables |
| `CombatUnit`, `CombatBuilding`, `SoldierAttacker`, `TowerAttacker`, `AttackTargetInfo` | `Rts.Combat.Model` | Vistas de combate |
| `CombatConfig`, `AttackStats` | `Rts.Combat.Configuration` | Parámetros tipados y validación |
| `CombatErrors` | `Rts.Combat` | `Combat.InvalidTarget`, `Combat.FriendlyTarget` |
| `EntityDamaged`, `EntityDestroyed` | `Rts.Shared.Events` | Eventos propuestos (D-009) |
| `WorldRect`, `GridSpec.ToWorldRect`, `IsWithinRange` | `Rts.Shared.Geometry` | Primitivas de distancia compartidas con Movement (D-019) |

Son internos, y Simulation no los usa: `ISoldierCombatCalculation` y `ITowerCombatCalculation`, los subcálculos paralelizables (02 §7).

## 2. Propiedad de datos

| Dato | Dueño | Notas |
|---|---|---|
| HP actual de unidades y edificios | Simulation (`WorldState`) | Combat propone daño agregado; nunca muta HP |
| `ReadyAtTick` de soldados y torres | Combat calcula / Simulation confirma | Es del atacante, no de la orden (D-022) |
| Orden `Attack` y su objetivo | Units / Movement + Simulation | Combat solo la lee |
| Persecución y alcanzabilidad | Units / Movement | Combat no mueve unidades (02 §5); ver O-001 |
| Estadísticas de ataque y **HP máximo de unidades** | **Combat** (D-021) | |
| **HP máximo de edificios** | **Construction** (D-021) | Es la meta de su progreso de obra |
| Evaluación de victoria | Simulation (fase 7) | Usa las destrucciones confirmadas |

## 3. Entrada (`CombatTickInput`)

Se construye desde el mismo snapshot que la de Economy. Las colecciones pueden venir en cualquier orden. El constructor exige IDs únicos en el espacio global (unidades y edificios juntos) y atacantes con referencias válidas.

- `Units`: todas las unidades vivas, con su posición oficial ya actualizada por Movement en este tick.
- `Buildings`: todos los edificios existentes, en obra o completados. El HP ya incluye la obra confirmada en la fase 3. `OperationalFromTick` es nulo en obra; si el edificio se completó en N, vale N+1.
- `Soldiers`: solo los soldados con orden `Attack` activa. Su posición y su dueño salen de `Units`.
- `Towers`: todas las torres **completadas**. Combat decide si ya son operativas (RECOMENDADO D-024: igual que en Economy, el chequeo de operatividad vive en el módulo y es comprobable en sus tests).
- `Grid`: la convención de grilla, para convertir huellas a coordenadas de mundo.

## 4. Reglas de cálculo que debe cumplir la implementación

Convenciones de distancia (DECIDIDO D-019, D-020):

- Unidad contra unidad: distancia entre puntos.
- Cualquier distancia que involucre un edificio: al **borde más cercano de su huella** (`Grid.ToWorldRect(footprint)`), tanto para un soldado que ataca un edificio como para el rango de una torre.
- Todas las comparaciones de rango son **inclusivas** (`distancia <= rango`) y se hacen con distancias al cuadrado.

Cooldown (RECOMENDADO D-022): un atacante está **listo** si `Tick >= ReadyAtTick`. Al atacar en el tick T, su nuevo `ReadyAtTick` es `T + CooldownTicks` del tipo. Como el valor pertenece al atacante, cambiar de objetivo no lo reinicia (02 §3). Un atacante nuevo arranca listo, así que su primer ataque es inmediato.

### Soldados

1. Se busca el objetivo en `Units ∪ Buildings`. Si no existe o es propio, el soldado va a `SoldiersToIdle`. Es un caso defensivo: normalmente Simulation ya canceló esas órdenes al confirmar la destrucción (§5).
2. Si está **listo** y el objetivo está **en rango** (`Soldier.Range`), produce `AttackPerformed` con `Soldier.Damage` y un `CooldownUpdate`.
3. Si no está listo o está fuera de rango, no hay ataque ni cambio. La persecución es de Movement.
4. Los soldados sin orden `Attack` no atacan nunca (02 §2).

### Torres

5. Solo actúan si `IsOperationalAt(Tick)`. Una torre completada en este mismo tick no ataca (02 §2).
6. Si está lista, sus candidatos son las **unidades enemigas** (nunca edificios) a distancia del borde de su huella menor o igual a `Tower.Range`.
7. Elige al candidato **más cercano**. Ante igualdad exacta de distancia al cuadrado, elige el de **menor `EntityId`**. Produce `AttackPerformed` con `Tower.Damage` y un `CooldownUpdate`.

### Agregación (02 §3, §7)

8. Todos los ataques se calculan sobre el snapshot: un atacante que muere en este tick **igual** ataca, lo que permite muertes mutuas y que una torre destruida dispare.
9. Por objetivo se suma el daño de todos sus atacantes en un único `TargetDamage(TargetId, HpBefore, TotalDamage)`. El HP resultante tiene piso cero y se calcula una sola vez; un objetivo recibe a lo sumo una destrucción.
10. Por cada `TargetDamage`, en orden de `TargetId`, se propone `EntityDamaged(id, HpAfter)` si sobrevive o `EntityDestroyed(id)` si llega a 0.
11. Todas las colecciones del resultado se ordenan por ID ascendente. El resultado no depende del orden de finalización de los subcálculos de soldados y torres.

Daño (RECOMENDADO D-023): un valor fijo por tipo de atacante, sin armadura ni modificadores por tipo de objetivo, porque 02 §10 habla de "ataque por tipo".

## 5. Lo que Simulation debe hacer con el resultado (commit)

- Aplicar `HpAfter` de cada `TargetDamage` una sola vez.
- Para cada destrucción:
  - quitar la entidad;
  - si es un edificio, liberar su huella y avanzar la `MapRevision`;
  - cancelar lo que dependía de ella: la cola y el trabajo de un productor (sin reembolso), los constructores y reparadores, y los soldados cuyo objetivo era esa entidad, que pasan a Idle (RECOMENDADO D-025).
- Un edificio `Completed` dañado sigue `Completed` (02 §4).
- Conservar los créditos de Economy del mismo tick, aunque el trabajador o la granja hayan muerto (06 §3).
- Aplicar los `CooldownUpdates` de los atacantes que sigan vivos y pasar a Idle los `SoldiersToIdle`.
- Fijar el `ReadyAtTick` inicial: el tick de aparición para un soldado nuevo y `OperationalFromTick` para una torre recién completada.
- Después del commit, la fase 7 evalúa la victoria: solo cae el Centro Principal de P1 ⇒ gana P2; solo cae el de P2 ⇒ gana P1; caen ambos en el mismo tick ⇒ `Draw`. Si la partida termina, no se ejecuta Production.
- PENDIENTE (Simulation): orden relativo entre los eventos de Economy y los de Combat al sellarlos en el mismo tick.

## 6. Validación del comando `Attack` (`ICombatRules.ValidateAttackTarget`)

Corre en la fase 1, después de que Simulation verificó la propiedad del atacante (`Common.NotOwner`).

| Caso | Resultado |
|---|---|
| Atacante no soldado | `Common.IncompatibleEntity` |
| Objetivo inexistente | `Common.EntityNotFound` |
| Objetivo que no es unidad ni edificio (árbol) | `Combat.InvalidTarget` |
| Objetivo propio | `Combat.FriendlyTarget` |
| Unidad o edificio enemigo, incluso en obra o granja en reparación | válido (`null`) |

Una orden inválida no altera la orden anterior (04 §2). La alcanzabilidad la decide Movement.

## 7. Configuración (`CombatConfig`)

| Parámetro | Validación | Valor |
|---|---|---|
| `UnitMaxHp` (Worker, Soldier) | > 0, todos los tipos | PENDIENTE |
| `Soldier`: `Damage`, `Range`, `CooldownTicks` | > 0; rango finito | PENDIENTE |
| `Tower`: `Damage`, `Range`, `CooldownTicks` | > 0; rango finito | PENDIENTE |

## 8. Pruebas de aceptación para la etapa de implementación

Estas pruebas de 02 §9 se escriben al implementar `ICombatCalculator`:

- soldado sin `Attack` no daña;
- ataque dentro y fuera de rango;
- objetivo inexistente ⇒ Idle;
- torre elige al más cercano, con desempate por ID;
- torre no daña edificios ni ataca recién completada;
- cooldown inicial inmediato y conservado al cambiar de objetivo;
- daño acumulado de varios atacantes con una sola destrucción;
- muerte mutua;
- torre que dispara y muere en el mismo tick;
- obra que pierde HP;
- rango medido al borde de la huella;
- resultado idéntico al permutar entradas y al forzar distinto orden de finalización de soldados y torres.

Las de persecución, edificio `Completed` que no vuelve a obra y doble destrucción de bases son de integración con Movement y Simulation.

## 9. Pendientes

- Valores de balance (§7).
- Tolerancias de punto flotante más allá de la comparación inclusiva.
- Eventos visuales de ataque: `AttackPerformed` existe como dato del resultado, pero publicarlo hacia Godot es PENDIENTE (02 §11.4).
- Tipos compartidos de persecución y cancelación de objetivo con Movement (02 §6, O-001).
