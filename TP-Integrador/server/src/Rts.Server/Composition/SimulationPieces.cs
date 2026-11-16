using Rts.Server.Diagnostics;
using Rts.Simulation;

namespace Rts.Server.Composition;

/// <summary>
/// Paso 5 del orden de construcción (contrato 10 §4): Simulation armada sobre las piezas de transporte. El motor queda adentro del
/// loop; de afuera solo hacen falta el loop (que GameSession controla como <c>ISimulationControl</c>) y el diagnóstico (al que la
/// composición le pide el resumen final, D-100). Crear no arranca los ticks: eso lo decide GameSession con <c>StartTicks</c>.
/// </summary>
internal sealed record SimulationPieces(ISimulationLoop Loop, LoggingSimulationDiagnostics Diagnostics);
