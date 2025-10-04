using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TlaPlugin.Tests;

public sealed class TestLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(this, logLevel, eventId, exception, formatter(state, exception)));
    }

    public sealed record LogEntry(TestLogger<T> Logger, LogLevel Level, EventId EventId, Exception? Exception, string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
