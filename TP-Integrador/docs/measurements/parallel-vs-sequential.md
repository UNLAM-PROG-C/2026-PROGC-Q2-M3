# Medición: Economy ∥ Combat en paralelo frente a secuencial

> Generado por `server/tools/Rts.Measurement` (D-101). Cada corrida sobrescribe este archivo.

- **Fecha:** 2026-11-15 19:16
- **Máquina:** 16 núcleos lógicos, Microsoft Windows 10.0.26200, .NET 10.0.14
- **Compilación:** Release
- **Ventana:** 20 ticks de calentamiento (descartados) + 120 medidos por modo, intercalados tick a tick
- **Escenario:** bloques de [árbol][trabajador][soldado P1][soldado P2], todo en rango; Economy, Combat y Movement reales, configuración de `server.example.json`

| Unidades | Secuencial 5+6 (mediana / prom) | Paralelo 5+6 (mediana / prom) | Speedup | Tick completo (sec / par) | Resultados |
|---:|---:|---:|---:|---:|:---|
| 30 | 0.054 / 0.095 ms | 0.088 / 0.092 ms | 0.62× | 0.109 / 0.146 ms | idénticos ✔ |
| 300 | 0.284 / 0.362 ms | 0.285 / 0.322 ms | 0.99× | 0.578 / 0.565 ms | idénticos ✔ |
| 1500 | 1.521 / 1.746 ms | 1.335 / 1.629 ms | 1.14× | 3.146 / 3.018 ms | idénticos ✔ |
| 3000 | 3.736 / 4.583 ms | 3.370 / 4.039 ms | 1.11× | 7.679 / 7.127 ms | idénticos ✔ |
| 6000 | 7.559 / 9.893 ms | 7.328 / 9.566 ms | 1.03× | 15.183 / 14.822 ms | idénticos ✔ |

**Cómo leerlo:**

- **5+6** = fase 5 (lanzar Economy y Combat) + fase 6 (barrera y commit). Se suman porque en paralelo la fase 5 solo lanza las tareas y la espera queda en la 6.
- **Speedup** = mediana secuencial / mediana paralela. Mayor que 1: gana el paralelo. El techo teórico es (Economy + Combat) / max(Economy, Combat), como mucho 2×.
- Con poca carga cada rama tarda microsegundos y el costo de coordinar dos tareas puede superar la ganancia: el paralelo conviene a partir de cierta carga.
- **Resultados** compara las publicaciones de los dos modos en cada tick (D-051).

**Por qué el speedup es ese** (promedios):

| Unidades | Economy + Combat (sec, fase 5) | Rama más lenta (par, fase 5 + barrera) | Commit (sec, fase 6) | Techo 5+6 | Speedup (prom) |
|---:|---:|---:|---:|---:|---:|
| 30 | 0.059 ms | 0.069 ms | 0.035 ms | 0.91× | 1.03× |
| 300 | 0.218 ms | 0.199 ms | 0.143 ms | 1.06× | 1.12× |
| 1500 | 0.965 ms | 0.809 ms | 0.779 ms | 1.10× | 1.07× |
| 3000 | 2.323 ms | 1.885 ms | 2.257 ms | 1.11× | 1.13× |
| 6000 | 5.055 ms | 4.809 ms | 4.836 ms | 1.03× | 1.03× |

Techo 5+6 = (Economy + Combat + commit) / (rama más lenta + commit): lo máximo que el paralelo podría ganar en este bloque si coordinar no costara nada. Cuanto más desparejas las ramas y más pesado el commit (que es secuencial en los dos modos), más bajo el techo. Es aproximado: la rama más lenta se estima en paralelo, donde también compite con la otra por memoria y caché. Toda esta tabla usa promedios, así que no se compara con el speedup de medianas de arriba.
