using CodeEditor.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CodeEditor.Infrastructure.Logging;

/// <summary>
/// Configures application-wide logging: a rolling file under %APPDATA%\CodeEditor\logs,
/// mirrored to the Output panel's "Log" channel.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Creates the logger factory used by the whole application.
    /// The caller owns the factory and must dispose it on shutdown to flush buffered log events.
    /// </summary>
    public static ILoggerFactory CreateLoggerFactory(IOutputService outputService)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodeEditor",
            "logs");

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "codeeditor-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSerilog(serilogLogger, dispose: true);
            builder.AddProvider(new OutputLoggerProvider(outputService));
        });
    }
}
