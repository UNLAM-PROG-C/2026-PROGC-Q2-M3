# 03 — Construction

> **Estado:** mecánicas cerradas; esquemas C# y balance por formalizar.
> **Etiquetas:** **DECIDIDO**, **RECOMENDADO**, **PENDIENTE**.
> **Referencias:** [Economy](01-economy.md), [Units / Movement](04-units-movement.md), [Combat](02-combat.md), [Simulation](06-simulation.md), [State Sync](09-state-sync.md).

## 1. Propósito y edificios — DECIDIDO

Gestionar la colocación, construcción progresiva y reparación de **capacidad de Granja**. Edificios de juego: **Centro Principal**, **Granja**, **Cuartel**, **Torre** y **Valla/empalizada**. El Centro Principal existe al comenzar la partida, **no** se construye durante ella. Los demás se colocan con madera si cumplen condiciones de mapa/trabajadores. Las vallas son elementos de bloqueo por celda individual; no hay puertas.

No gestiona movimiento, combate, economía de recursos ni red por su cuenta. Simulation confirma los cambios.

## 2. Colocación y ocupación — DECIDIDO

- Mapa fijo con **grilla** de ocupación para edificios y vallas. Cada tipo tiene huella configurable/definida por diseño.
- Los edificios pueden ubicarse en cualquier zona válida del mapa, incluidas zonas que después bloqueen caminos, siempre dentro de límites y reglas de ocupación.
- **Se permite cerrar caminos completamente** mediante muros o edificios; no se exige preservar una ruta universal.
- No se coloca sobre otra estructura, muro, unidad presente en la huella en el momento de colocar, ni celdas reservadas de árboles (visibles o agotados).
- **Los árboles no bloquean movimiento**, pero sí prohiben construcción en sus celdas reservadas.
- Un edificio ocupa su huella **desde que se acepta la colocación**, aunque aún no esté terminado. Cambia `MapRevision` y se publica el obstáculo para Godot.
- Debe existir al menos **un trabajador seleccionado** que pueda alcanzar una posición de interacción con el edificio **después** de considerar su colocación hipotética.
- Si un grupo de trabajadores intenta construir, se aceptan solo los trabajadores compatibles y alcanzables; los demás conservan la orden anterior. Se confirma aceptación parcial cuando corresponda.
- Validación, cobro de madera y colocación son un **único commit lógico atómico**. Si falla cualquier condición obligatoria, no hay gasto ni obra parcial.
- No existe cancelación manual de edificios ya colocados; sí puede abandonarse o cambiarse la orden de los constructores.

## 3. Construcción progresiva — DECIDIDO

- Al colocarse, el edificio tiene un **porcentaje inicial positivo configurable** de HP máximo (ejemplo de 10 % era ilustrativo).
- Para una obra en progreso, el **HP representa también el progreso de construcción**.
- Los trabajadores válidos dentro de rango aportan HP de construcción **por tick**; varios trabajadores suman aportes.
- Un trabajador puede unirse después; otro puede marcharse sin perder progreso existente.
- Si Combat daña un edificio en obra, **reduce su HP de construcción**, y por eso puede demorar la finalización.
- Al alcanzar `MaxHp`, el edificio queda `Completed`. No vuelve a «en construcción» si después recibe daño sin destruirse.
- Se vuelve **operativo desde el tick siguiente** a su finalización, si sigue existiendo: un Cuartel recién completado no produce instantáneamente; una Torre recién completada no ataca en ese mismo tick; una Granja no produce comida todavía.
- Tras completar la obra, los trabajadores asignados a construir pasan a `Idle`.
- Un edificio a 0 HP se destruye, desaparece, libera espacio y emite cambio de ocupación/revisión del mapa.
- Los edificios terminados **no tienen reparación general de HP de combate**.

## 4. Reparación exclusiva de granjas agotadas — DECIDIDO

- **Solo** para una granja `Depleted` por consumir sus **5 tandas** de comida. No se aplica a otras estructuras ni se utiliza para curar daño.
- Se paga madera **una vez al iniciar** la reparación; no se vuelve a cobrar al sumar trabajadores.
- Requiere progreso temporal por contribuciones de trabajadores válidos; su suma es determinista.
- Si todos los trabajadores se retiran, el progreso se **conserva**.
- Al completar la reparación, la granja recupera **5 tandas de capacidad productiva**, y se reinicia su temporizador de producción.
- No recupera HP de Combat.
- Si la granja se destruye, el pago y progreso restantes se pierden; los trabajadores dejan la tarea.
- La acción `RepairFarm` no es válida cuando la granja aún tiene tandas disponibles.

## 5. Relación con Movement y mapa — DECIDIDO

- Los trabajadores deben poder alcanzar una **celda/posición de interacción** cercana a la huella de la construcción, no necesariamente el centro del edificio.
- Validar alcanzabilidad **contra la huella hipotética** evita admitir trabajos imposibles por autoinclusión del obstáculo.
- Units / Movement provee comprobaciones de alcanzabilidad y movimiento oficial; Construction decide condiciones geométricas de colocación y contribución de obra.
- Al colocar o destruir edificios/muros, `MapRevision` cambia. Rutas previamente válidas pueden entrar en `RepathPending`, gestionado por Units / Movement y Simulation.
- Solo los edificios/muros son obstáculos dinámicos de navegación; árboles y unidades no forman obstáculos de rutas (aunque una unidad **presente al colocar** impide colocar en su celda).

## 6. Contratos conceptuales — PROPUESTOS, NO FIRMAS C# APROBADAS

| Operación / DTO | Responsabilidad |
|---|---|
| `PlacementInput` | Tipo, propietario, huella, ubicación, trabajadores, mapa/ocupación, árbol reservado y alcanzabilidad proyectada |
| `EvaluatePlacement` | Validar construcción y generar propuesta de colocación/trabajadores aceptados |
| `CalculateBuildProgress` | Calcular HP aportado por trabajadores en tick y finalizaciones |
| `EvaluateRepairStart` | Evaluar estado agotado, costo y trabajadores para iniciar reparación |
| `CalculateRepairProgress` | Calcular progreso acumulado y finalización de reparación productiva |
| `ConstructionTickResult` | Deltas de HP/estado, cambios de mapa y eventos propuestos |

El costo se consulta a Economy mediante coordinación de Simulation: Construction no descuenta saldo directamente. Los identificadores/resultados individuales de órdenes grupales deben incluirse en contratos compartidos.

## 7. Orden de ejecución y concurrencia — DECIDIDO

- Se procesan comandos y Movement; luego **Construction** actualiza obra/reparaciones.
- Después se crea el **TickSnapshot** para Economy y Combat, que calculan concurrentemente.
- Por eso, un edificio completado en Construction puede sufrir daño en Combat de ese mismo tick; ya quedó marcado `Completed`, pero no es operativo hasta N+1.
- Un edificio destruido por Combat se elimina antes de Production. No puede iniciar entrenamiento si ya fue destruido.
- Solo Simulation confirma HP, ocupación, gasto y estado; no se necesitan locks particulares de construcción sobre `WorldState`.
- Varios trabajadores construyendo/reparando son **contribuciones agregadas**, no múltiples escritores concurrentes del objeto de edificio.

## 8. Casos límite e invariantes — DECIDIDO

- Ninguna colocación fuera del mapa, sobre estructura, árbol reservado o unidad que ocupe la huella.
- Si ningún trabajador puede acceder a una posición de construcción tras colocar, rechazo completo.
- Si algunos trabajadores son alcanzables, aceptación parcial del grupo.
- Nunca se descuenta madera sin colocar la obra correspondiente.
- La huella se ocupa desde el inicio y libera solo al destruirse.
- Un trabajador que abandona conserva el progreso aportado al edificio.
- El edificio `Completed` no vuelve al estado «obra» por perder HP sin llegar a cero.
- Granjas agotadas permanecen en mapa; reparación de su capacidad no cura HP.
- Se puede cerrar una ruta completamente; Movement deberá resolver imposibilidad o repath.

## 9. Pruebas de aceptación

**DECIDIDO:** colocación válida con gasto único; rechazos por recursos, límites, superposición, árbol agotado y unidad presente; imposibilidad de acceso tras colocación; grupo parcialmente alcanzable; muralla que cierra camino; ocupación durante obra; múltiples constructores; progreso conservado; destrucción de obra; finalización diferida a N+1; ataque que reduce HP de obra; edificio Completed dañado no vuelve a obra; granja agotada reparable y no reparable antes; costo de reparación único; reparación interrumpida; sin curar HP; destrucción libera mapa y cambia revisión.

**RECOMENDADO:** integración determinista Construction → Snapshot → Combat, y colocación de vallas que fuerza repath de soldados/trabajadores.

## 10. Configuración — DECIDIDO / PENDIENTE

**Configurables aprobados:** costos por edificio, huellas, HP máximo, porcentaje inicial positivo de obra, HP por trabajador/tick, tiempo/trabajo requerido para reparar granja, costo de reparación, rangos de interacción. **PENDIENTE:** valores concretos de balance y tamaño de mapa/huellas, tolerancias espaciales y DTO de ocupación.

## 11. Recomendaciones para los seis aspectos aún no formalizados — RECOMENDADO

1. **Arquitectura/dependencias:** separar validación pura de colocación, cálculo de progreso y confirmación en Simulation; usar comprobación de alcanzabilidad de Movement como dato de entrada, no llamar directamente a Godot.
2. **Modelos:** `BuildingState` con `Type`, `OwnerId`, `Footprint`, `Hp`, `ConstructionStatus`, `OperationalFromTick`; para Granja, `FarmRepairProgress` separado de HP. Reservas de árbol en `GameMap`.
3. **Interfaces/DTOs:** resultados de colocación con costo, huella, trabajadores aceptados/rechazados y razones; propuestas de progreso de ticks con IDs; reglas de error estructuradas.
4. **JSON:** exponer `BuildingPlaced`, `BuildingProgressChanged`, `BuildingCompleted`, `EntityDestroyed` y `MapRevision` vía State Sync, sin serializar el modelo interno directamente.
5. **Configuración:** tabla tipada por tipo de edificio, costo/HP/huella/progreso; validar que inicio >0 y <= MaxHp, y que edificios no queden con huella vacía.
6. **Organización del trabajo:** desarrollar primero colocación contra mapa simplificado, después construcción progresiva y reparación, luego integrar Movement (alcanzabilidad), Economy (gasto) y Combat (HP/destrucción).

## 12. Pendientes explícitos

**PENDIENTE:** DTOs C# definitivos, convención de grilla/huellas y coordenadas, costo y estadísticas definitivas, interfaz de alcanzabilidad, errores tipados y política concreta de precisión geométrica. Estas decisiones no deben improvisarse por un agente aislado.
