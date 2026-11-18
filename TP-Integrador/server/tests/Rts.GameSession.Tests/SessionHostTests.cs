using Microsoft.Extensions.Logging;
using Rts.Shared.Ids;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

/// <summary>
/// El host real con Networking, State Sync y Simulation falsos (contrato 07 §11, "con el host"): orden de los efectos, detención
/// antes de <c>MatchEnded</c>, timer de Ready, apagado y varios hilos publicando a la vez.
/// </summary>
public class SessionHostTests : StateMachineTestBase
{
  private const int ConcurrentClients = 16;
  private const int FirstClientId = 100;
  private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

  [Fact]
  public async Task TwoReadyPlayers_StartTheMatch_WithEffectsInTheContractOrder()
  {
    HostRig rig = NewRig();
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);

    await StartMatchAsync(rig);

    Assert.Equal<string>(
        [
          $"Send:{ConnA.Value}:PlayerAssigned",
          $"Send:{ConnB.Value}:PlayerAssigned",
          "InitializeMatch",
          "PublishInitialState:1",
          $"Send:{ConnA.Value}:MatchStarted",
          $"Send:{ConnB.Value}:MatchStarted",
          "StartTicks",
        ],
        rig.Log.Entries);
    await EndAsync(shutdown, run);
  }

  [Fact]
  public async Task ADisconnectInRunning_StopsTicksFirst_AndMatchEndedOnlyAfterTheyStopped()
  {
    HostRig rig = NewRig();
    rig.Simulation.StopGate = new TaskCompletionSource<TickId>(TaskCreationOptions.RunContinuationsAsynchronously);
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);
    await StartMatchAsync(rig);

    rig.Inbox.PostConnectionClosed(ConnA);
    await rig.WaitForAsync("StopTicks");

    Assert.False(rig.Store.AcceptsGameplay);
    Assert.False(rig.Log.Contains($"Send:{ConnB.Value}:MatchEnded"));
    rig.Simulation.StopGate.SetResult(FinalTick);
    MatchResult result = await run.WaitAsync(WaitLimit);
    Assert.Equal(MatchResult.PlayerDisconnected(PlayerId.Two, FinalTick), result);
    AssertInOrder(rig, "StopTicks", $"Send:{ConnB.Value}:MatchEnded", "Drain");
    Assert.False(rig.Log.Contains($"Send:{ConnA.Value}:MatchEnded"));
  }

  [Fact]
  public async Task ASimulationResult_EndsTheMatch_AndAnnouncesItToBothPlayers()
  {
    HostRig rig = NewRig();
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);
    await StartMatchAsync(rig);
    MatchResult worldResult = MatchResult.BaseDestroyed(PlayerId.One, FinalTick);

    rig.Inbox.OnSimulationResult(worldResult);

    Assert.Equal(worldResult, await run.WaitAsync(WaitLimit));
    Assert.True(rig.Log.Contains($"Send:{ConnA.Value}:MatchEnded"));
    Assert.True(rig.Log.Contains($"Send:{ConnB.Value}:MatchEnded"));
    AssertInOrder(rig, $"Send:{ConnB.Value}:MatchEnded", "Drain");
  }

  [Fact]
  public async Task AFailingInitialization_EndsWithACriticalError_AndNeverStartsTicks()
  {
    HostRig rig = NewRig();
    rig.Simulation.InitializeFailure = new InvalidOperationException("sin mapa");
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);

    rig.Inbox.TryPostHello(ConnA);
    rig.Inbox.TryPostHello(ConnB);

    Assert.Equal(MatchResult.CriticalError(null), await run.WaitAsync(WaitLimit));
    Assert.False(rig.Log.Contains("StartTicks"));
    Assert.False(rig.Log.Contains("StopTicks"));
    Assert.True(rig.Log.Contains("Drain"));
  }

  [Fact]
  public async Task AFailingStartOfTicks_EndsWithACriticalError_InsteadOfHangingInRunning()
  {
    HostRig rig = NewRig();
    rig.Simulation.StartFailure = new InvalidOperationException("no arranca");
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);

    await StartMatchAsync(rig);

    Assert.Equal(MatchResult.CriticalError(null), await run.WaitAsync(WaitLimit));
    Assert.True(rig.Log.Contains($"Send:{ConnA.Value}:MatchEnded"));
  }

  [Fact]
  public async Task TheReadyTimeout_IsScheduledWithTheClock_AndExpelsWhoDidNotConfirm()
  {
    HostRig rig = NewRig();
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);
    rig.Inbox.TryPostHello(ConnA);
    rig.Inbox.TryPostHello(ConnB);
    await rig.WaitForAsync("PublishInitialState:1");
    rig.Inbox.TryPostClientReady(ConnA, Init1);
    await HostRig.WaitUntilAsync(() => rig.Store.Current.Player1?.IsReady == true);

    Assert.Equal<TimeSpan>([ReadyTimeout], rig.Clock.DueTimes);
    rig.Clock.FireAll();
    await rig.WaitForAsync($"Send:{ConnA.Value}:StartAborted");

    Assert.True(rig.Log.Contains($"Close:{ConnB.Value}:{SessionErrors.ReadyTimeoutExpired}"));
    Assert.Equal(SessionStatus.WaitingForPlayers, rig.Store.Status);
    await EndAsync(shutdown, run);
  }

  [Fact]
  public async Task CancellingTheToken_WhileWaiting_ShutsDownInOrderWithoutAMatch()
  {
    HostRig rig = NewRig();
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);

    MatchResult result = await EndAsync(shutdown, run);

    Assert.Equal(MatchResult.ServerShutdown(null), result);
    Assert.Equal<string>(["Drain"], rig.Log.Entries);
  }

  [Fact]
  public async Task CancellingTheToken_WhileRunning_StopsTicksBeforeFixingTheResult()
  {
    HostRig rig = NewRig();
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);
    await StartMatchAsync(rig);

    MatchResult result = await EndAsync(shutdown, run);

    Assert.Equal(MatchResult.ServerShutdown(FinalTick), result);
    AssertInOrder(rig, "StopTicks", $"Send:{ConnA.Value}:MatchEnded", "Drain");
  }

  [Fact]
  public async Task ADrainThatFails_DoesNotKeepTheSessionFromEnding()
  {
    HostRig rig = NewRig();
    rig.Effects.DrainFails = true;
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);

    MatchResult result = await EndAsync(shutdown, run);

    Assert.Equal(MatchResult.ServerShutdown(null), result);
  }

  [Fact]
  public async Task ManyClientsSayingHelloAtOnce_GetExactlyTwoSlots_AndTheRestAreClosed()
  {
    HostRig rig = NewRig();
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);

    Parallel.For(0, ConcurrentClients, i => rig.Inbox.TryPostHello(new ConnectionId(FirstClientId + i)));
    await HostRig.WaitUntilAsync(() => rig.Log.CountContaining("Close:") == ConcurrentClients - 2);

    Assert.Equal(2, rig.Log.CountContaining(":PlayerAssigned"));
    Assert.Equal(1, rig.Log.CountContaining("InitializeMatch"));
    Assert.NotNull(rig.Store.Current.Player1);
    Assert.NotNull(rig.Store.Current.Player2);
    Assert.NotEqual(rig.Store.Current.Player1!.Connection, rig.Store.Current.Player2!.Connection);
    await EndAsync(shutdown, run);
  }

  [Fact]
  public async Task TheSession_CanOnlyRunOnce()
  {
    HostRig rig = NewRig();
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> first = rig.Host.RunAsync(shutdown.Token);

    await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync(CancellationToken.None));
    await EndAsync(shutdown, first);
  }

  [Fact]
  public void Constructor_RejectsMissingDependenciesAndInvalidConfiguration()
  {
    HostRig rig = NewRig();
    var invalid = rig.Config with { ReadyTimeout = TimeSpan.Zero };

    Assert.Throws<ArgumentNullException>(() => new SessionHost(null!, rig.Store, rig.Machine, rig.Effects, rig.Simulation, rig.Config, rig.Clock, rig.Logger));
    Assert.Throws<ArgumentNullException>(() => new SessionHost(rig.Inbox, rig.Store, rig.Machine, rig.Effects, rig.Simulation, rig.Config, null!, rig.Logger));
    Assert.Throws<ArgumentNullException>(() => new SessionHost(rig.Inbox, rig.Store, rig.Machine, rig.Effects, rig.Simulation, rig.Config, rig.Clock, null!));
    Assert.Throws<ArgumentException>(() => new SessionHost(rig.Inbox, rig.Store, rig.Machine, rig.Effects, rig.Simulation, invalid, rig.Clock, rig.Logger));
  }

  [Fact]
  public async Task TheLog_TellsTheStoryOfAMatch_StatesPlayersAndTheOfficialResult()
  {
    HostRig rig = NewRig();
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);
    await StartMatchAsync(rig);

    rig.Inbox.OnSimulationResult(MatchResult.BaseDestroyed(PlayerId.One, FinalTick));
    await run.WaitAsync(WaitLimit);

    string[] story = [.. rig.Logger.Entries.Where(entry => entry.Level == LogLevel.Information).Select(entry => entry.Message)];
    AssertLoggedInOrder(
        story, "WaitingForPlayers → Starting", "Starting → Running", "Running → Ending", "resultado oficial Victory", "Ending → Ended");
    Assert.Equal(2, story.Count(message => message.Contains("es el jugador", StringComparison.Ordinal)));
    Assert.DoesNotContain(rig.Logger.Entries, entry => entry.Level >= LogLevel.Warning);
  }

  [Fact]
  public async Task AFailingInitialization_IsLoggedAsAnErrorWithItsException()
  {
    HostRig rig = NewRig();
    var failure = new InvalidOperationException("sin mapa");
    rig.Simulation.InitializeFailure = failure;
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);

    rig.Inbox.TryPostHello(ConnA);
    rig.Inbox.TryPostHello(ConnB);
    await run.WaitAsync(WaitLimit);

    AssertLoggedError(rig, failure);
  }

  [Fact]
  public async Task AFailingStartOfTicks_IsLoggedAsAnErrorWithItsException()
  {
    HostRig rig = NewRig();
    var failure = new InvalidOperationException("no arranca");
    rig.Simulation.StartFailure = failure;
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);

    await StartMatchAsync(rig);
    await run.WaitAsync(WaitLimit);

    AssertLoggedError(rig, failure);
  }

  [Fact]
  public async Task AFailingStopOfTicks_StillFixesTheResultWithoutFinalTick_AndIsLoggedAsAnError()
  {
    HostRig rig = NewRig();
    var failure = new InvalidOperationException("no se detiene");
    rig.Simulation.StopFailure = failure;
    Task<MatchResult> run = rig.Host.RunAsync(CancellationToken.None);
    await StartMatchAsync(rig);

    rig.Inbox.PostConnectionClosed(ConnA);

    // La sesión no puede quedar esperando: se informa SimulationStopped(null) igual (07 §8).
    Assert.Equal(MatchResult.PlayerDisconnected(PlayerId.Two, null), await run.WaitAsync(WaitLimit));
    AssertLoggedError(rig, failure);
    Assert.Contains(rig.Logger.Entries, entry => entry.Message.Contains("se pidió detener los ticks", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AFailingDrain_IsLoggedAsAnError_AndTheSessionStillEnds()
  {
    HostRig rig = NewRig();
    rig.Effects.DrainFails = true;
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);

    await EndAsync(shutdown, run);

    LogEntry error = Assert.Single(rig.Logger.Entries, entry => entry.Level == LogLevel.Error);
    Assert.IsType<InvalidOperationException>(error.Exception);
  }

  [Fact]
  public async Task ARequestWithoutEffect_IsLoggedOnlyAtDebugLevel()
  {
    HostRig rig = NewRig();
    using var shutdown = new CancellationTokenSource();
    Task<MatchResult> run = rig.Host.RunAsync(shutdown.Token);

    rig.Inbox.TryPostHello(ConnA);
    rig.Inbox.TryPostHello(ConnA); // Hello duplicado: se rechaza sin efecto.
    await rig.WaitForAsync($"Send:{ConnA.Value}:SessionRequestRejected");
    await EndAsync(shutdown, run);

    LogEntry rejected = Assert.Single(rig.Logger.Entries, entry => entry.Message.Contains("sin efecto", StringComparison.Ordinal));
    Assert.Equal(LogLevel.Debug, rejected.Level);
  }

  private static void AssertLoggedError(HostRig rig, Exception failure)
  {
    LogEntry error = Assert.Single(rig.Logger.Entries, entry => entry.Level == LogLevel.Error);
    Assert.Same(failure, error.Exception);
  }

  /// <summary>Cada fragmento aparece en algún mensaje, y en el mismo orden en que se pasaron.</summary>
  private static void AssertLoggedInOrder(string[] messages, params string[] fragments)
  {
    int[] positions = [.. fragments.Select(fragment => Array.FindIndex(messages, message => message.Contains(fragment, StringComparison.Ordinal)))];
    Assert.All(positions, position => Assert.True(position >= 0));
    Assert.Equal(positions.Order(), positions);
  }

  private static HostRig NewRig() => new(Session, ReadyTimeout, QueueCapacity);

  /// <summary>Dos clientes se registran y confirman: espera hasta que Simulation recibe <c>StartTicks</c>.</summary>
  private static async Task StartMatchAsync(HostRig rig)
  {
    rig.Inbox.TryPostHello(ConnA);
    rig.Inbox.TryPostHello(ConnB);
    await rig.WaitForAsync("PublishInitialState:1");
    rig.Inbox.TryPostClientReady(ConnA, Init1);
    rig.Inbox.TryPostClientReady(ConnB, Init1);
    await rig.WaitForAsync("StartTicks");
  }

  /// <summary>Pide el apagado como lo haría Ctrl+C y espera el resultado de la sesión.</summary>
  private static async Task<MatchResult> EndAsync(CancellationTokenSource shutdown, Task<MatchResult> run)
  {
    await shutdown.CancelAsync();
    return await run.WaitAsync(WaitLimit);
  }

  /// <summary>Cada entrada tiene que estar en la bitácora, y en el mismo orden en que se pasaron.</summary>
  private static void AssertInOrder(HostRig rig, params string[] entries)
  {
    int[] positions = entries.Select(rig.Log.IndexOf).ToArray();
    Assert.All(positions, position => Assert.True(position >= 0));
    Assert.Equal(positions.Order(), positions);
  }
}
