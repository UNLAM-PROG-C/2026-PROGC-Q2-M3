using Microsoft.Extensions.Logging;

namespace Rts.Networking.Tests;

/// <summary>Una línea de log tal como la vería quien la lee: nivel, categoría, mensaje ya formateado y la excepción, si la hubo.</summary>
internal sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>
/// Fábrica de loggers de prueba: en vez de escribir, anota cada entrada. Se usa desde varios hilos (receptores, escritores y el
/// servidor), por eso protege su lista.
/// </summary>
internal sealed class ListLoggerFactory : ILoggerFactory
{
  private readonly object _lock = new();
  private readonly List<LogEntry> _entries = [];

  public IReadOnlyList<LogEntry> Entries
  {
    get
    {
      lock (_lock)
      {
        return [.. _entries];
      }
    }
  }

  public ILogger CreateLogger(string categoryName) => new ListLogger(this, categoryName);

  public void AddProvider(ILoggerProvider provider)
  {
  }

  public void Dispose()
  {
  }

  private void Add(LogEntry entry)
  {
    lock (_lock)
    {
      _entries.Add(entry);
    }
  }

  private sealed class ListLogger(ListLoggerFactory owner, string category) : ILogger
  {
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        owner.Add(new LogEntry(logLevel, category, formatter(state, exception), exception));
  }
}
