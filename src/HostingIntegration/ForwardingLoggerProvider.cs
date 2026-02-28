using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.InMemoryWebServer;

// Logger provider that forwards logs to a single target logger.
internal sealed class ForwardingLoggerProvider(ILogger targetLogger) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new ForwardingLogger(categoryName, targetLogger);

    public void Dispose() { }

    private sealed class ForwardingLogger(string categoryName, ILogger targetLogger) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => targetLogger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
            => targetLogger.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => targetLogger.Log(logLevel, eventId, state, exception, (s, ex) => $"[{categoryName}] {formatter(s, ex)}");
    }
}

