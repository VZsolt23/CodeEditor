using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CodeEditor.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Infrastructure.Terminal;

/// <summary>
/// Hosts shell processes with redirected stdio (the simple pre-ConPTY approach:
/// fine for builds, git, and dotnet; full-screen TUI apps are not supported).
/// Tracks live sessions and kills them on dispose so no shells outlive the app.
/// </summary>
public sealed class TerminalService : ITerminalService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TerminalService> _logger;
    private readonly object _lock = new();
    private readonly List<TerminalSession> _sessions = [];
    private bool _disposed;

    public TerminalService(ISettingsService settingsService, ILogger<TerminalService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public ITerminalSession Start(string? workingDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var shell = _settingsService.Settings.TerminalShell;
        var session = TerminalSession.Start(shell, workingDirectory);
        _logger.LogInformation("Started terminal shell {Shell} in {Directory}", shell, session.WorkingDirectory);

        lock (_lock)
        {
            _sessions.RemoveAll(existing => !existing.IsRunning);
            _sessions.Add(session);
        }

        return session;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_lock)
        {
            foreach (var session in _sessions)
            {
                session.Dispose();
            }

            _sessions.Clear();
        }
    }

    private sealed class TerminalSession : ITerminalSession
    {
        private readonly Process _process;
        private bool _disposed;

        private TerminalSession(Process process, string workingDirectory)
        {
            _process = process;
            WorkingDirectory = workingDirectory;
        }

        public string WorkingDirectory { get; }

        public string ShellName => Path.GetFileName(_process.StartInfo.FileName);

        public bool IsRunning
        {
            get
            {
                try
                {
                    return !_process.HasExited;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    return false;
                }
            }
        }

        public event EventHandler<string>? OutputReceived;

        public event EventHandler<int>? Exited;

        public static TerminalSession Start(string shell, string? workingDirectory)
        {
            var directory = workingDirectory is not null && Directory.Exists(workingDirectory)
                ? workingDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // cmd.exe defaults to the OEM codepage; switch the session to UTF-8 so
            // the redirected streams and our encodings agree.
            var isCmd = Path.GetFileNameWithoutExtension(shell)
                .Equals("cmd", StringComparison.OrdinalIgnoreCase);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = isCmd ? "/K chcp 65001>nul" : string.Empty,
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                },
                EnableRaisingEvents = true,
            };

            var session = new TerminalSession(process, directory);
            process.Exited += (_, _) => session.OnProcessExited();
            process.Start();
            process.StandardInput.AutoFlush = true;

            _ = session.PumpAsync(process.StandardOutput);
            _ = session.PumpAsync(process.StandardError);
            return session;
        }

        public void WriteLine(string text)
        {
            try
            {
                _process.StandardInput.WriteLine(text);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The shell died mid-write; the Exited event tells the user.
            }
        }

        public void Kill()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Already gone.
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Kill();
            _process.Dispose();
        }

        private async Task PumpAsync(StreamReader reader)
        {
            var buffer = new char[4096];
            try
            {
                while (true)
                {
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        return;
                    }

                    OutputReceived?.Invoke(this, new string(buffer, 0, read));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Stream closed because the process exited or the session was disposed.
            }
        }

        private void OnProcessExited()
        {
            int exitCode;
            try
            {
                exitCode = _process.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                exitCode = -1;
            }

            Exited?.Invoke(this, exitCode);
        }
    }
}
