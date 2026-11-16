using System.Text.Json;
using Rts.Protocol;
using Rts.Shared.Entities;
using Rts.Shared.Events;
using Rts.Shared.Geometry;
using Rts.Shared.Ids;
using Rts.Shared.Resources;
using Rts.Shared.Time;
using Rts.Shared.World;
using Rts.StateSync;
using Rts.StateSync.Configuration;
using Rts.StateSync.Projection;

namespace Rts.Networking.Tests.Integration;

/// <summary>
/// Prueba de integración «Cliente lento» (work-plan §4, AGENTS §7), compartida entre State Sync y Networking: el
/// <see cref="StateSyncPublisher"/> real publica sobre el <see cref="NetworkServer"/> real, con dos clientes TCP. P2 deja de leer su
/// socket. <c>Publish</c> nunca espera por él, P2 se cierra por saturación (D-073) y P1 sigue recibiendo todos los lotes en orden.
/// </summary>
public class SlowClientTests
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

  /// <summary><c>Publish</c> solo proyecta y encola: si tardara esto, estaría esperando a un socket.</summary>
  private static readonly TimeSpan PublishBudget = TimeSpan.FromSeconds(1);

  /// <summary>Cola chica: el lado del servidor se satura apenas se llenan los buffers TCP del cliente que no lee.</summary>
  private const int QueueCapacity = 16;

  /// <summary>Lotes grandes, para llenar esos buffers con pocos ticks.</summary>
  private const int EventsPerBatch = 200;

  /// <summary>Atraso máximo de P1, en ticks. Es menor que la cola: P1 hace de cliente que sigue el ritmo de Simulation.</summary>
  private const int MaxLagTicks = 4;

  /// <summary>Tope: si P2 no se satura antes, la prueba falla en lugar de quedarse publicando.</summary>
  private const int MaxTicks = 20_000;

  /// <summary>Ticks que se publican después de cerrar P2, para mostrar que P1 sigue recibiendo.</summary>
  private const int TicksAfterSaturation = 20;

  /// <summary>Valor de relleno del catálogo: solo viaja en el InitialState, que esta prueba no publica.</summary>
  private const int Unused = 1;

  [Fact]
  public async Task SlowClient_DoesNotBlockPublishingNorTheOtherClient()
  {
    await using var harness = new NetworkServerHarness(config => config with
    {
      OutboundQueueCapacity = QueueCapacity,
      PingIdleInterval = Patience, // sin heartbeat durante la prueba: P2 se cierra por saturación y no por falta de Pong
    });
    await harness.StartAsync();
    var (fast, fastId, _, slowId) = await NetworkServerIntegrationTests.ConnectTwoPlayersAsync(harness);
    var publisher = new StateSyncPublisher(new StateProjector(new StateSyncConfig(), TickRate.Default), harness.Server, Catalog());
    using var reader = new BatchReader(fast);

    long tick = 0;
    while (!harness.Session.Closed.Contains(slowId))
    {
      Assert.True(tick < MaxTicks, "P2 nunca se saturó.");
      await PublishPacedAsync(publisher, reader, ++tick);
    }

    for (int i = 0; i < TicksAfterSaturation; i++)
    {
      await PublishPacedAsync(publisher, reader, ++tick);
    }
    await reader.WaitForTickAsync(tick);

    Assert.Equal(Enumerable.Range(1, (int)tick).Select(t => (long)t), reader.Ticks);
    Assert.DoesNotContain(fastId, harness.Session.Closed);
    Assert.Contains(harness.Loggers.Entries, e => e.Message.Contains(NetworkErrors.OutboundSaturated.Value, StringComparison.Ordinal));
  }

  /// <summary>
  /// Publica un tick como lo haría Simulation, en otro hilo y con tiempo límite: si <c>Publish</c> se bloqueara, la prueba falla.
  /// Después espera a que P1 no se atrase más de <see cref="MaxLagTicks"/>: P2 nunca frena la publicación, pero sin esa espera la
  /// prueba publicaría más rápido de lo que cualquier cliente lee y también saturaría a P1.
  /// </summary>
  private static async Task PublishPacedAsync(StateSyncPublisher publisher, BatchReader reader, long tick)
  {
    await Task.Run(() => publisher.Publish(Batch(new TickId(tick)))).WaitAsync(PublishBudget);
    await reader.WaitForTickAsync(tick - MaxLagTicks);
  }

  /// <summary>Un tick con <see cref="EventsPerBatch"/> eventos públicos y sin posiciones.</summary>
  private static TickPublication Batch(TickId tick) => new(
      tick,
      MapRevision.Initial,
      [.. Enumerable.Range(0, EventsPerBatch).Select(i => new DomainEvent(new EventStamp(tick, i), new EntityDamaged(new EntityId(i + 1), Unused)))],
      default,
      default,
      null);

  private static GameCatalog Catalog() => new(
      TickRate.Default,
      [.. Enum.GetValues<UnitType>().Select(t => new UnitCatalogEntry(t, Unused, ResourceAmount.Zero, Unused, Unused, null))],
      [.. Enum.GetValues<BuildingType>().Select(t => new BuildingCatalogEntry(t, Unused, new FootprintSize(Unused, Unused), null, null))],
      ResourceAmount.Zero,
      Unused,
      Unused,
      Unused);

  /// <summary>Lee en segundo plano las líneas de un cliente y anota el tick de cada <c>StateEvents</c>.</summary>
  private sealed class BatchReader : IDisposable
  {
    private readonly List<long> _ticks = [];
    private readonly SemaphoreSlim _progress = new(0);
    private readonly Task _reading;

    public BatchReader(TestClient client) => _reading = Task.Run(() => ReadAsync(client));

    public IReadOnlyList<long> Ticks
    {
      get
      {
        lock (_ticks)
        {
          return [.. _ticks];
        }
      }
    }

    /// <summary>Espera hasta haber recibido el lote de ese tick (o uno posterior).</summary>
    public async Task WaitForTickAsync(long tick)
    {
      while (LastTick < tick)
      {
        Assert.False(_reading.IsCompleted, "El cliente rápido dejó de recibir.");
        Assert.True(await _progress.WaitAsync(Patience), $"El cliente rápido no recibió el tick {tick} a tiempo.");
      }
    }

    public void Dispose() => _progress.Dispose();

    private long LastTick
    {
      get
      {
        lock (_ticks)
        {
          return _ticks.Count == 0 ? 0 : _ticks[^1];
        }
      }
    }

    private async Task ReadAsync(TestClient client)
    {
      while (await client.ReadLineAsync() is { } line)
      {
        using JsonDocument message = JsonDocument.Parse(line);
        if (message.RootElement.GetProperty("type").GetString() != ProtocolMessageTypes.StateEvents)
        {
          continue;
        }
        lock (_ticks)
        {
          _ticks.Add(message.RootElement.GetProperty("payload").GetProperty("tick").GetInt64());
        }
        _progress.Release();
      }
    }
  }
}
