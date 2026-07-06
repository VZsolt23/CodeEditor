using CodeEditor.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Infrastructure.Logging;

/// <summary>
/// Forwards log entries to the "Log" output channel so the Output panel shows
/// the same events as the log file (Information and above).
/// </summary>
public sealed class OutputLoggerProvider : ILoggerProvider
{
    private readonly IOutputChannel _channel;

    public OutputLoggerProvider(IOutputService outputService)
    {
        ArgumentNullException.ThrowIfNull(outputService);
        _channel = outputService.GetOrCreateChannel("Log");
    }

    public ILogger CreateLogger(string categoryName) => new OutputLogger(_channel, categoryName);

    public void Dispose()
    {
        // The channel is owned by the output service; nothing to release here.
    }

    private sealed class OutputLogger(IOutputChannel channel, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.Now:HH:mm:ss.fff} [{Abbreviate(logLevel)}] {ShortCategory(categoryName)}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            channel.AppendLine(line);
        }

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => level.ToString().ToUpperInvariant(),
        };

        private static string ShortCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 ? category[(lastDot + 1)..] : category;
        }
    }
}
