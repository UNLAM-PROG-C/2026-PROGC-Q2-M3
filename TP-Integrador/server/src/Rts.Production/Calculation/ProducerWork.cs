using Rts.Production.Model;
using Rts.Shared.Collections;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.Production.Calculation;

/// <summary>
/// Copia de trabajo de un productor durante <b>un</b> cálculo de la fase 8. Es mutable y local: nace al empezar el cálculo y se
/// descarta al terminar, así la entrada (<see cref="ProducerState"/>, inmutable) nunca se modifica y la función sigue siendo pura.
/// Guarda además cómo estaba el productor al entrar, para saber qué cambió y si estaba libre.
/// </summary>
internal sealed class ProducerWork
{
  private readonly List<ProductionRequest> _pending;

  public ProducerWork(ProducerState original)
  {
    ArgumentNullException.ThrowIfNull(original);
    Original = original;
    Job = original.ActiveJob;
    _pending = [.. original.Pending];
    WasFree = original.ActiveJob is null;
  }

  /// <summary>El productor tal como entró a la fase.</summary>
  public ProducerState Original { get; }

  /// <summary>Tarea activa ahora (puede haber cambiado durante este cálculo).</summary>
  public ProductionJob? Job { get; private set; }

  /// <summary>
  /// No tenía tarea activa <b>al entrar</b>. Solo estos productores pueden iniciar un entrenamiento en esta fase (05 §4): uno
  /// que acaba de liberar su tarea por una aparición espera al tick siguiente.
  /// </summary>
  public bool WasFree { get; }

  /// <summary>La solicitud más antigua de la cola, o <c>null</c> si no hay.</summary>
  public ProductionRequest? Head => _pending.Count > 0 ? _pending[0] : null;

  /// <summary>La unidad ya está entrenada (<c>RemainingTicks == 0</c>) y espera aparecer, ahora o desde un tick anterior.</summary>
  public bool IsFinished => Job is { RemainingTicks: 0 };

  /// <summary>El productor como queda al terminar el cálculo.</summary>
  public ProducerState Current => new(
      Original.ProducerId, Original.Owner, Original.ProducerType, Original.Footprint, Original.OperationalFromTick, Job, _pending.ToEquatableArray());

  /// <summary>Cambió algo, incluido el progreso de la tarea: Simulation necesita el estado nuevo para confirmarlo.</summary>
  public bool HasChanged => Current != Original;

  /// <summary>
  /// Cambió el estado <b>discreto</b> (estado, unidad activa o cola), no solo el contador de ticks. Solo entonces se publica
  /// <see cref="ProductionChanged"/>: no se emite en cada tick de progreso (D-042).
  /// </summary>
  public bool DiscreteStateChanged => DiscreteState.Of(Original) != DiscreteState.Of(Current);

  /// <summary>Resta un tick a una tarea en entrenamiento. Una tarea ya terminada (esperando aparecer) no avanza.</summary>
  public void Advance()
  {
    if (Job is { RemainingTicks: > 0 } job)
    {
      Job = new ProductionJob(job.Request, job.PaidFood, job.StartedAtTick, job.RemainingTicks - 1);
    }
  }

  /// <summary>La unidad apareció: el productor queda sin tarea activa.</summary>
  public void Release() => Job = null;

  /// <summary>Saca la solicitud más antigua de la cola y la convierte en la tarea activa, ya pagada.</summary>
  public void Start(int paidFood, TickId tick, int durationTicks)
  {
    ProductionRequest head = _pending[0];
    _pending.RemoveAt(0);
    Job = new ProductionJob(head, paidFood, tick, durationTicks);
  }

  /// <summary>El evento con el estado actual del productor, visible solo para su dueño (05 §4, D-042).</summary>
  public ProductionChanged ToEvent() => new(
      Original.ProducerId,
      Original.Owner,
      Current.Status,
      Job?.Request.UnitType,
      Job?.RemainingTicks,
      PendingUnitTypes(Current));

  private static EquatableArray<UnitType> PendingUnitTypes(ProducerState state) =>
      state.Pending.Select(request => request.UnitType).ToEquatableArray();

  private readonly record struct DiscreteState(ProducerStatus Status, UnitType? ActiveUnit, EquatableArray<UnitType> PendingUnits)
  {
    public static DiscreteState Of(ProducerState state) => new(state.Status, state.ActiveJob?.Request.UnitType, PendingUnitTypes(state));
  }
}
