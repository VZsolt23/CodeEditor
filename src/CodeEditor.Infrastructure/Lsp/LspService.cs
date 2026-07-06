using System.ComponentModel;
using System.Diagnostics;
using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Completion;
using CodeEditor.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace CodeEditor.Infrastructure.Lsp;

/// <summary>
/// Hosts the TypeScript/JavaScript language server process (configured via the
/// <c>typeScriptServerCommand</c> setting, spawned with <c>--stdio</c>) behind
/// <see cref="ILspService"/>. The server starts lazily on the first document
/// notification and is restarted per workspace. All operations are best-effort:
/// a missing server or a dead process reports via <see cref="StatusChanged"/>
/// instead of throwing.
/// </summary>
public sealed class LspService : ILspService, IDisposable
{
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settingsService;
    private readonly ILogger<LspService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;
    private LspServerConnection? _connection;
    private string? _rootPath;
    private bool _startFailed;
    private bool _stopping;

    public LspService(ISettingsService settingsService, ILogger<LspService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<LspDiagnosticsEvent>? DiagnosticsPublished;

    public async Task SetWorkspaceAsync(string? rootPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            _rootPath = rootPath;
            _startFailed = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NotifyDocumentOpenedAsync(
        string filePath, string languageId, string text, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureStartedAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (connection is not null)
            {
                await connection.OpenDocumentAsync(filePath, languageId, text).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsServerFailure(ex))
        {
            OnServerFailed(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NotifyDocumentChangedAsync(
        string filePath, string text, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { } connection)
            {
                await connection.ChangeDocumentAsync(filePath, text).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsServerFailure(ex))
        {
            OnServerFailed(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NotifyDocumentClosedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { } connection)
            {
                await connection.CloseDocumentAsync(filePath).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsServerFailure(ex))
        {
            OnServerFailed(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetHoverAsync(
        string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        // Take the connection under the gate, then do the round-trip outside it so a
        // hover cannot block document-sync notifications (or vice versa). Never starts
        // the server — hover only works once a document opened it.
        LspServerConnection? connection;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            connection = _connection;
        }
        finally
        {
            _gate.Release();
        }

        if (connection is null)
        {
            return null;
        }

        try
        {
            return await connection.RequestHoverAsync(filePath, line, character, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsServerFailure(ex) || ex is OperationCanceledException)
        {
            // A failed request is left for the notification paths to tear down.
            return null;
        }
    }

    public async Task<IReadOnlyList<CompletionItemInfo>?> GetCompletionsAsync(
        string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        // Same pattern as hover: take the connection under the gate, request outside it.
        LspServerConnection? connection;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            connection = _connection;
        }
        finally
        {
            _gate.Release();
        }

        if (connection is null)
        {
            return null;
        }

        try
        {
            return await connection.RequestCompletionAsync(filePath, line, character, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsServerFailure(ex) || ex is OperationCanceledException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        KillProcess();
        _gate.Dispose();
    }

    /// <summary>Starts the server if needed. Must be called while holding the gate.</summary>
    private async Task<LspServerConnection?> EnsureStartedAsync(string fallbackPath, CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        if (_startFailed)
        {
            return null;
        }

        var command = _settingsService.Settings.TypeScriptServerCommand;
        var rootPath = _rootPath ?? Path.GetDirectoryName(fallbackPath) ?? Environment.CurrentDirectory;

        var process = StartProcess(command, rootPath);
        if (process is null)
        {
            _startFailed = true;
            Report($"TypeScript/JavaScript language services unavailable: '{command}' was not found. "
                   + "Install it with: npm install -g typescript-language-server typescript");
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(InitializeTimeout);
            _connection = await LspServerConnection.CreateAsync(
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                rootPath,
                diagnosticSource: "typescript",
                onDiagnostics: (path, diagnostics) => RaiseDiagnostics(path, diagnostics),
                timeout.Token).ConfigureAwait(false);
            _process = process;
            Report("TypeScript/JavaScript language server ready.");
            return _connection;
        }
        catch (Exception ex) when (ex is OperationCanceledException || IsServerFailure(ex))
        {
            _startFailed = true;
            _logger.LogError(ex, "Language server '{Command}' failed to initialize", command);
            Report($"TypeScript/JavaScript language server failed to start: {ex.Message}");
            TryKill(process);
            process.Dispose();
            return null;
        }
    }

    private Process? StartProcess(string command, string rootPath)
    {
        // npm on Windows installs a .cmd shim, which Process.Start (UseShellExecute=false)
        // does not resolve from the bare name — try both.
        foreach (var fileName in (string[])[command, command + ".cmd"])
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = "--stdio",
                    WorkingDirectory = rootPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };

            try
            {
                process.Start();
            }
            catch (Win32Exception)
            {
                process.Dispose();
                continue;
            }

            process.Exited += OnProcessExited;
            DrainStandardError(process);
            return process;
        }

        return null;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopping)
        {
            return;
        }

        _startFailed = true;
        _logger.LogWarning("TypeScript/JavaScript language server exited unexpectedly");
        Report("TypeScript/JavaScript language server exited unexpectedly.");
    }

    private void DrainStandardError(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    _logger.LogDebug("LSP stderr: {Line}", line);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Process ended; drain complete.
            }
        });
    }

    /// <summary>Stops the server politely, then kills it. Must be called while holding the gate.</summary>
    private async Task StopCoreAsync()
    {
        if (_connection is null && _process is null)
        {
            return;
        }

        _stopping = true;
        try
        {
            if (_connection is { } connection)
            {
                using var timeout = new CancellationTokenSource(ShutdownTimeout);
                await connection.ShutdownAsync(timeout.Token).ConfigureAwait(false);
                connection.Dispose();
            }

            KillProcess();
        }
        finally
        {
            _connection = null;
            _stopping = false;
        }
    }

    private void KillProcess()
    {
        if (_process is not { } process)
        {
            return;
        }

        _process = null;
        TryKill(process);
        process.Dispose();
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already exited.
        }
    }

    private void OnServerFailed(Exception ex)
    {
        _logger.LogWarning(ex, "Language server communication failed");
        _connection?.Dispose();
        _connection = null;
        KillProcess();
        _startFailed = true;
        Report("TypeScript/JavaScript language server connection lost.");
    }

    private static bool IsServerFailure(Exception ex)
        => ex is RemoteRpcException or IOException or ObjectDisposedException or InvalidOperationException;

    private void RaiseDiagnostics(string filePath, IReadOnlyList<DiagnosticItem> diagnostics)
        => DiagnosticsPublished?.Invoke(this, new LspDiagnosticsEvent(filePath, diagnostics));

    private void Report(string status)
    {
        _logger.LogInformation("{Status}", status);
        StatusChanged?.Invoke(this, status);
    }
}
