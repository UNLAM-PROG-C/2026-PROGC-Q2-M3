using Microsoft.Extensions.Logging;

namespace Rts.Server.Tests.Diagnostics;

/// <summary>Un mensaje escrito en el log, ya formateado.</summary>
internal sealed record LoggedEntry(string Category, LogLevel Level, string Message, Exception? Exception);

/// <summary>Fábrica de loggers de prueba: guarda, en orden y desde cualquier hilo, todo lo que se escribe en cualquier categoría.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
  private readonly object _gate = new();
  private readonly List<LoggedEntry> _entries = [];

  public IReadOnlyList<LoggedEntry> Entries
  {
    get
    {
      lock (_gate)
      {
        return [.. _entries];
      }
    }
  }

  public IReadOnlyList<LoggedEntry> At(LogLevel level) => [.. Entries.Where(entry => entry.Level == level)];

  public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

  public void AddProvider(ILoggerProvider provider)
  {
  }

  public void Dispose()
  {
  }

  private void Add(LoggedEntry entry)
  {
    lock (_gate)
    {
      _entries.Add(entry);
    }
  }

  private sealed class RecordingLogger(RecordingLoggerFactory owner, string category) : ILogger
  {
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        owner.Add(new LoggedEntry(category, logLevel, formatter(state, exception), exception));
  }
}
