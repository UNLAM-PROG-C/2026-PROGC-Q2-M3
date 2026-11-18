using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rts.GameSession.Actions;
using Rts.GameSession.Configuration;
using Rts.GameSession.Model;
using Rts.Shared.Errors;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;
using Rts.Shared.World;

namespace Rts.GameSession.Tests;

/// <summary>
/// Bitácora compartida y segura para hilos: el host, Networking falso y Simulation falso anotan acá lo que reciben, así las pruebas
/// verifican el <b>orden global</b> de los efectos (por ejemplo, que <c>MatchEnded</c> sale después de detener los ticks).
/// </summary>
internal sealed class CallLog
{
  private readonly ConcurrentQueue<string> _entries = new();

  public IReadOnlyList<string> Entries => _entries.ToArray();

  public void Add(string entry) => _entries.Enqueue(entry);

  public int IndexOf(string entry) => Array.IndexOf(_entries.ToArray(), entry);

  public bool Contains(string entry) => IndexOf(entry) >= 0;

  public int CountContaining(string fragment) => _entries.Count(entry => entry.Contains(fragment, StringComparison.Ordinal));
}

/// <summary>Networking y State Sync falsos: solo anotan lo que se les pide. <see cref="DrainFails"/> simula sockets muertos.</summary>
internal sealed class FakeEffects(CallLog log) : ISessionEffects
{
  public bool DrainFails { get; set; }

  public void Send(ConnectionId connection, ISessionMessage message) => log.Add($"Send:{connection.Value}:{message.GetType().Name}");

  public void Close(ConnectionId connection, ErrorCode reason) => log.Add($"Close:{connection.Value}:{reason}");

  public void PublishInitialState(SessionId session, InitializationId initialization, WorldView world, SessionRecipient player1, SessionRecipient player2) =>
      log.Add($"PublishInitialState:{initialization.Value}");

  public Task DrainAndCloseAllAsync()
  {
    log.Add("Drain");
    return DrainFails ? Task.FromException(new InvalidOperationException("socket muerto")) : Task.CompletedTask;
  }
}

/// <summary>
/// Simulation falso. <see cref="StopGate"/> deja a la prueba decidir cuándo termina <c>StopTicksAsync</c>; sin compuerta, termina
/// enseguida con <see cref="FinalTick"/>.
/// </summary>
internal sealed class FakeSimulation(CallLog log) : ISimulationControl
{
  private const int GridSize = 4;
  private const double CellSize = 1.0;

  public static readonly TickId FinalTick = new(40);

  private static readonly WorldView EmptyWorld = new(TickId.Zero, new MapView(new GridSpec(CellSize, GridSize, GridSize), MapRevision.Initial), [], [], [], [], []);

  public Exception? InitializeFailure { get; set; }

  public Exception? StartFailure { get; set; }

  public TaskCompletionSource<TickId>? StopGate { get; set; }

  public Exception? StopFailure { get; set; }

  public WorldView InitializeMatch()
  {
    log.Add("InitializeMatch");
    return InitializeFailure is null ? EmptyWorld : throw InitializeFailure;
  }

  public void StartTicks()
  {
    log.Add("StartTicks");
    if (StartFailure is not null)
    {
      throw StartFailure;
    }
  }

  public Task<TickId> StopTicksAsync()
  {
    log.Add("StopTicks");
    return StopFailure is null ? StopGate?.Task ?? Task.FromResult(FinalTick) : Task.FromException<TickId>(StopFailure);
  }
}

/// <summary>Una línea del log, ya formateada como la escribiría el Server.</summary>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>Logger falso y seguro para hilos: guarda lo que el host registra para que las pruebas lo lean (D-096).</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
  private readonly ConcurrentQueue<LogEntry> _entries = new();

  public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

  public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
      _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
}

/// <summary>Reloj manual: no espera nada. Guarda los timers y la prueba los dispara cuando quiere.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
  private readonly List<ManualTimer> _timers = [];

  public IReadOnlyList<TimeSpan> DueTimes
  {
    get
    {
      lock (_timers)
      {
        return _timers.Select(timer => timer.DueTime).ToArray();
      }
    }
  }

  public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
  {
    var timer = new ManualTimer(callback, state, dueTime);
    lock (_timers)
    {
      _timers.Add(timer);
    }

    return timer;
  }

  public void FireAll()
  {
    ManualTimer[] snapshot;
    lock (_timers)
    {
      snapshot = _timers.ToArray();
    }

    foreach (ManualTimer timer in snapshot)
    {
      timer.Fire();
    }
  }
}

internal sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
{
  public TimeSpan DueTime => dueTime;

  public void Fire()
  {
    callback(state);
  }

  public bool Change(TimeSpan newDueTime, TimeSpan newPeriod) => true;

  public void Dispose()
  {
  }

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>El host real con sus vecinos falsos, ya conectados como lo haría la composición del servidor (contrato 10 §4).</summary>
internal sealed class HostRig
{
  private const int PollIntervalMilliseconds = 1;
  private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

  public HostRig(SessionId session, TimeSpan readyTimeout, int queueCapacity)
  {
    Config = new GameSessionConfig { ReadyTimeout = readyTimeout, EventQueueCapacity = queueCapacity };
    Effects = new FakeEffects(Log);
    Simulation = new FakeSimulation(Log);
    Inbox = new SessionInbox(queueCapacity);
    Store = new SessionStateStore(GameSessionState.Initial(session));
    Machine = new SessionStateMachine(Config);
    Host = new SessionHost(Inbox, Store, Machine, Effects, Simulation, Config, Clock, Logger);
  }

  public CallLog Log { get; } = new();

  public CapturingLogger<SessionHost> Logger { get; } = new();

  public ManualTimeProvider Clock { get; } = new();

  public GameSessionConfig Config { get; }

  public FakeEffects Effects { get; }

  public FakeSimulation Simulation { get; }

  public SessionInbox Inbox { get; }

  public SessionStateStore Store { get; }

  public SessionStateMachine Machine { get; }

  public SessionHost Host { get; }

  public Task WaitForAsync(string entry) => WaitUntilAsync(() => Log.Contains(entry));

  /// <summary>Espera a que se cumpla una condición producida por otro hilo; falla con <see cref="TimeoutException"/> si no llega.</summary>
  public static async Task WaitUntilAsync(Func<bool> condition)
  {
    using var timeout = new CancellationTokenSource(WaitLimit);
    while (!condition())
    {
      if (timeout.IsCancellationRequested)
      {
        throw new TimeoutException("La condición esperada no se cumplió a tiempo.");
      }

      await Task.Delay(PollIntervalMilliseconds);
    }
  }
}
