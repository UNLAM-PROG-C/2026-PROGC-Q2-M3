using Rts.Networking.Parsing;
using Rts.Shared.Commands;
using Rts.Shared.Errors;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.Networking.Admission;

/// <summary>
/// Admisión de comandos de gameplay de <b>una</b> conexión (08 §5). Ejecuta los cuatro pasos en orden y se detiene en el primero
/// que falla, de modo que cada paso solo corre si los anteriores pasaron:
/// <list type="number">
/// <item><see cref="ISessionGate.AcceptsGameplay"/> → <c>Common.MatchNotRunning</c>.</item>
/// <item><see cref="ISessionGate.GetPlayer"/> → <c>Network.NotRegistered</c>. Es la única fuente del <see cref="PlayerId"/>.</item>
/// <item><see cref="ICommandRateLimiter.TryAcquire"/> → <c>Network.RateLimited</c>, la <b>única</b> que cuenta como falta (D-074).</item>
/// <item><see cref="ICommandSink.Admit"/> → <c>Common.CommandQueueFull</c>. La ficha del paso 3 ya se gastó.</item>
/// </list>
/// Un rechazo en los pasos 1 y 2 no gasta ficha. Un comando rechazado puede reintentarse con el mismo <c>messageId</c> (D-075).
/// <para>
/// No valida reglas de gameplay, no detecta duplicados (lo hace Simulation, D-043), no asigna la <c>Sequence</c> (lo hace la cola) ni
/// envía el <c>CommandRejected</c>: devuelve una <see cref="AdmissionDecision"/> y el receptor actúa.
/// </para>
/// <para>
/// Hay una por conexión y la usa solo su receptor, porque el limitador de comandos no es seguro para hilos. El gate y la cola
/// sí lo son (los comparten ambos receptores).
/// </para>
/// </summary>
internal sealed class CommandAdmitter
{
  private readonly ConnectionId _connection;
  private readonly ISessionGate _gate;
  private readonly ICommandRateLimiter _rateLimiter;
  private readonly ICommandSink _sink;

  public CommandAdmitter(ConnectionId connection, ISessionGate gate, ICommandRateLimiter rateLimiter, ICommandSink sink)
  {
    ArgumentNullException.ThrowIfNull(gate);
    ArgumentNullException.ThrowIfNull(rateLimiter);
    ArgumentNullException.ThrowIfNull(sink);

    _connection = connection;
    _gate = gate;
    _rateLimiter = rateLimiter;
    _sink = sink;
  }

  public AdmissionDecision Admit(GameplayMessage message)
  {
    ArgumentNullException.ThrowIfNull(message);

    // Paso 1: la partida acepta comandos. No es culpa del cliente.
    if (!_gate.AcceptsGameplay)
    {
      return AdmissionDecision.Rejected(CommonErrors.MatchNotRunning, isViolation: false);
    }

    // Paso 2: la conexión tiene plaza. El PlayerId sale solo de acá, nunca del mensaje.
    if (_gate.GetPlayer(_connection) is not { } player)
    {
      return AdmissionDecision.Rejected(NetworkErrors.NotRegistered, isViolation: false);
    }

    // Paso 3: queda una ficha. Es el único rechazo que cuenta como falta.
    if (!_rateLimiter.TryAcquire())
    {
      return AdmissionDecision.Rejected(NetworkErrors.RateLimited, isViolation: true);
    }

    // Paso 4: la cola global asigna la Sequence y encola, en una sola operación atómica.
    CommandAdmission admission = _sink.Admit(player, message.ClientCommandId, message.Command);

    return admission.IsAdmitted
        ? AdmissionDecision.Admitted(admission.Sequence!.Value)
        : AdmissionDecision.Rejected(admission.Error!.Value, isViolation: false);
  }
}
