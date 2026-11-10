# 02 — Combat

> **Estado:** definición funcional cerrada; contrato técnico conceptual, balance y firmas definitivas pendientes.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Simulation](06-simulation.md), [Units / Movement](04-units-movement.md), [Construction](03-construction.md), [Economy](01-economy.md), [State Sync](09-state-sync.md).

## 1. Propósito — DECIDIDO

Resolver los ataques de **soldados** y **torres**, determinar objetivos válidos, rangos, cooldowns y daño, y producir resultados de combate que Simulation confirma sobre un único `WorldState`. Combat **no** mueve unidades, no modifica HP directamente en el estado oficial, no deduce recursos ni gestiona TCP.

## 2. Unidades, objetivos y reglas — DECIDIDO

### Soldados

- Son atacantes **cuerpo a cuerpo**.
- Pueden atacar unidades enemigas (trabajadores y soldados) y **edificios enemigos**, incluidos edificios en construcción o granjas en reparación.
- **Solo atacan por una orden explícita** `Attack`. No existe autoataque de soldados inactivos.
- Si el objetivo se mueve, el soldado lo persigue según Units / Movement hasta entrar en rango.
- Si el objetivo sale de rango, continúa persiguiéndolo; si es destruido o resulta inalcanzable, pasa a `Idle`.
- Pueden cambiar de objetivo por otra orden válida sin reiniciar indebidamente su cooldown.

### Torres

- Una torre **completada y operativa** ataca automáticamente unidades enemigas dentro de su rango: trabajadores o soldados.
- Selecciona la **unidad enemiga más cercana**; ante empate, el menor ID según orden determinista.
- **No ataca edificios**.
- No hay cálculo de línea de visión: basta la distancia/rango.
- Una torre que termina de construirse en tick N no ataca hasta **N+1**.
- Si una torre era operativa al crear el snapshot y muere en Combat de ese tick, **su ataque previsto sigue contando**.

### Trabajadores y otras entidades

- Los trabajadores **no atacan**.
- Los edificios pueden ser objetivos válidos de soldados y sufrir daño, incluso incompletos. No existe recuperación general de HP de combate en edificios completados.

## 3. Cooldowns y daño — DECIDIDO

- Cada atacante dispone de cooldown medido en **ticks**, configurable por tipo.
- Si el atacante está listo y tiene objetivo válido dentro del rango, **el primer ataque se ejecuta inmediatamente**: no hay espera inicial adicional.
- El cambio de orden/objetivo **no reinicia** el cooldown.
- Todos los ataques del mismo tick se calculan a partir del **mismo snapshot inmutable**.
- Los daños contra un objetivo se **acumulan** y Simulation aplica el HP resultante (con piso cero) **una sola vez** en el commit correspondiente.
- Dos soldados pueden matarse mutuamente en el mismo tick: ambos ataques son válidos.
- Determinar bajas no debe depender del orden de finalización de tareas de ataque.

## 4. Construcción y daño — DECIDIDO

- Un edificio recién colocado tiene HP inicial **positivo y configurable como fracción de su HP máximo** (p. ej. 10 % fue solo ilustrativo).
- Mientras está en construcción, Construction aumenta HP de obra; Combat puede reducirlo.
- La fase Construction precede al snapshot de Combat; por tanto, Combat ve el progreso de construcción ya confirmado en ese tick.
- Si Construction alcanzó `MaxHp` y marcó el edificio `Completed`, esa finalización **permanece** aunque el edificio reciba daño después en Combat, siempre que no sea destruido. La operatividad comienza recién en el tick siguiente.
- Un edificio destruido desaparece, libera su ocupación del mapa y provoca la cancelación de operaciones que dependían de él.
- Reparar la **capacidad de producción** de una Granja no significa recuperar HP de daño de combate.

## 5. Interacción con Movement y Victory — DECIDIDO

- Las posiciones para rango son las **oficiales del servidor**, ya actualizadas por Movement al comienzo del tick.
- Combat no calcula rutas ni modifica posiciones; SoldierAttack persigue el objetivo mediante órdenes/estado coordinados con Units / Movement.
- El rango para atacar edificios considera la **huella** según las reglas de interacción de Movement; no se exige alcanzar el centro geométrico del edificio.
- Tras aplicar resultados de Combat, Simulation ejecuta `Victory Rules`: si se destruye solo el Centro Principal de P1, gana P2; si se destruye solo el de P2, gana P1; si ambos caen en el **mismo tick**, hay empate.
- Si la partida termina, Production no avanza ese tick.

## 6. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| Operación / DTO | Responsabilidad |
|---|---|
| `CombatTickInput` | Tick; atacantes, objetivos, posiciones oficiales, HP, estado operativo, cooldowns, config; todos inmutables |
| `CalculateTick(input)` | Resolver blancos, ataques, daño y timers sin mutar estado |
| `CombatTickResult` | Ataques efectuados, daño agregado por entidad, nuevos cooldowns, candidatos a destrucción y eventos propuestos |
| `SoldierCombatCalculation` | Resolver ataques explícitos válidos de soldados |
| `TowerCombatCalculation` | Resolver autoataques de torres operativas |
| `ValidateAttackTarget` | Evaluación tipada de objetivos autorizados y estado de orden |

El contrato exacto de persecución y cancelación de objetivo es transversal a Units / Movement y Simulation: **PENDIENTE** definir tipos compartidos, no las reglas funcionales.

## 7. Concurrencia y sincronización — DECIDIDO

- Combat y Economy ejecutan **al mismo tiempo** sus cálculos sobre el snapshot congelado.
- Como subcálculos de Combat, soldados y torres pueden procesarse **concurrentemente**.
- Ninguno escribe `WorldState`; Combat devuelve propuestas inmutables.
- Simulation espera la **barrera** de Economy + Combat, valida ambos resultados y confirma en orden determinista.
- El resultado es independiente del orden real de finalización de las tareas.
- Un atacante muerto en el mismo tick **no pierde** el ataque que le correspondía según snapshot.
- Una torre completada ese mismo tick **todavía no es operativa** para el cálculo de Combat.

## 8. Casos límite e invariantes — DECIDIDO

- Un soldado Idle no ataca sin orden.
- Un trabajador nunca produce daño.
- Torre solo apunta unidades enemigas y elige blanco por distancia/ID.
- Daño no reduce HP por debajo de cero.
- Múltiples atacantes sobre un blanco generan una única destrucción efectiva.
- Cambio de objetivo no otorga ataques adicionales por reset de cooldown.
- Destrucción de objetivo o inalcanzabilidad deja al soldado Idle.
- Ataque y muerte simultáneos son posibles.
- Destrucción de ambos Centros Principales en el mismo tick produce empate, no un ganador arbitrario.
- No hay fuego amigo ni daño de soldados a sus propias estructuras.
- El daño de Combat puede interferir con una obra/una granja en reparación, sin quitar recursos ya acreditados por Economy en el mismo tick.

## 9. Pruebas de aceptación

**DECIDIDO:** soldado sin Attack no daña; ataque cuerpo a cuerpo dentro/fuera de rango; persecución; objetivo destruido/inalcanzable; torre selecciona cercano con desempate por ID; torre no daña edificio; trabajador no ataca; cooldown inicial inmediato; conservar cooldown al cambiar objetivo; daño acumulado de varios atacantes; muerte mutua; torre dispara y muere en mismo tick; edificio en obra recibe daño y pierde progreso de HP; edificio completado no retrocede a obra; doble destrucción de bases produce empate; cálculo no cambia si tareas terminan en distinto orden.

**RECOMENDADO:** pruebas deterministas de replay de `CombatTickInput` que comparen resultados completos, incluyendo eventos y cooldowns.

## 10. Configuración — DECIDIDO / PENDIENTE

**Configurables aprobados:** HP máximo por unidad/edificio, ataque por tipo, alcance, velocidades y cooldowns relacionados, HP inicial de obra (definido desde Construction), tiempos asociados. **PENDIENTE:** valores numéricos reales; reglas exactas de redondeo/distancia/precisión flotante; firmas y errores tipados.

## 11. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** Combat solo consume estados tipados de atacantes/objetivos; Units / Movement mantiene persecución y movilidad, Simulation coordina sus consecuencias.
2. **Modelos:** distinguir `CombatStats`/`CooldownState` de `UnitState`, `BuildingHpState` y `BuildingOperationalSinceTick`; usar ID estable para ordenar y resolver empates.
3. **Interfaces/DTOs:** representar cada ataque propuesto y daño total; incluir `TickId` y validaciones explícitas; nunca exponer `WorldState` mutable.
4. **JSON:** State Sync publica HP y destrucciones confirmadas, no intenciones de ataque no confirmadas. Si se agregan animaciones de ataque, crear eventos visuales separados sin autoridad de daño.
5. **Configuración:** centralizar estadística por tipo, rango y cooldown en ticks; documentar medición de distancia a huellas y tolerancias.
6. **Organización del trabajo:** probar Combat puro con snapshots simulados; luego integrar Movement (persecución), Construction (obra dañable) y Simulation (victoria).

## 12. Pendientes explícitos

**PENDIENTE:** contratos C# exactos, valores numéricos de balance, codificación de estados `Attacking/Idle`, representación de cooldowns, payload definitivo de daños/eventos, detalles de tolerancia geométrica; ninguna de estas propuestas cambia las reglas aprobadas.
