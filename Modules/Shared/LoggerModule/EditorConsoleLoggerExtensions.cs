using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Log;

public static class EditorConsoleLoggerExtensions
{
    public static ILoggingBuilder AddEditorConsole(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProvider(new EditorConsoleLoggerProvider());
        return builder;
    }

    private sealed class EditorConsoleLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => EditorConsoleLogger.Instance;

        public void Dispose()
        {
        }
    }

    private sealed class EditorConsoleLogger : ILogger
    {
        public static EditorConsoleLogger Instance { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            Console.Out.WriteLine(EditorConsoleLogProtocol.Serialize(
                DateTimeOffset.UtcNow,
                (int)logLevel,
                formatter(state, exception)));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
