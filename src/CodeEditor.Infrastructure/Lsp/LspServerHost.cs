using System.ComponentModel;
using System.Diagnostics;
using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Completion;
using CodeEditor.Core.Diagnostics;
using CodeEditor.Core.Documents;
using CodeEditor.Core.Workspace;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace CodeEditor.Infrastructure.Lsp;

/// <summary>
/// Describes one language server: the languages it serves, how to find its
/// command in settings, and the labels used in status messages/diagnostics.
/// </summary>
/// <param name="DisplayName">Human name for status messages (e.g. "TypeScript/JavaScript").</param>
/// <param name="DiagnosticSource">Source tag stamped on this server's diagnostics (e.g. "typescript").</param>
/// <param name="Languages">Language ids this server handles.</param>
/// <param name="GetCommand">Reads the server command from settings.</param>
/// <param name="InstallHint">One-line install instruction shown when the command is missing.</param>
internal sealed record LspServerDescriptor(
    string DisplayName,
    string DiagnosticSource,
    IReadOnlySet<string> Languages,
    Func<ISettingsService, string> GetCommand,
    string InstallHint);

/// <summary>
/// Hosts a single language server process (spawned with <c>--stdio</c>) and its
/// JSON-RPC connection. Starts lazily on the first document, restarts per
/// workspace, and is killed on dispose. All operations are best-effort — a
/// missing server or a dead process reports status instead of throwing.
/// </summary>
internal sealed class LspServerHost : IDisposable
{
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly LspServerDescriptor _descriptor;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;
    private readonly Action<string, IReadOnlyList<DiagnosticItem>> _onDiagnostics;
    private readonly Action<string> _report;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;
    private LspServerConnection? _connection;
    private string? _rootPath;
    private bool _startFailed;
    private bool _stopping;

    public LspServerHost(
        LspServerDescriptor descriptor,
        ISettingsService settingsService,
        ILogger logger,
        Action<string, IReadOnlyList<DiagnosticItem>> onDiagnostics,
        Action<string> report)
    {
        _descriptor = descriptor;
        _settingsService = settingsService;
        _logger = logger;
        _onDiagnostics = onDiagnostics;
        _report = report;
    }

    /// <summary>Whether this server handles documents of <paramref name="languageId"/>.</summary>
    public bool Handles(string languageId) => _descriptor.Languages.Contains(languageId);

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

    public async Task OpenDocumentAsync(string filePath, string languageId, string text, CancellationToken cancellationToken = default)
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

    public async Task ChangeDocumentAsync(string filePath, string text, CancellationToken cancellationToken = default)
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

    public async Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken = default)
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

    public Task<string?> GetHoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => RequestAsync(connection => connection.RequestHoverAsync(filePath, line, character, cancellationToken), (string?)null, cancellationToken);

    public Task<IReadOnlyList<CompletionItemInfo>?> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => RequestAsync(connection => connection.RequestCompletionAsync(filePath, line, character, cancellationToken), (IReadOnlyList<CompletionItemInfo>?)null, cancellationToken);

    public Task<IReadOnlyList<SearchMatch>> GetDefinitionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => RequestAsync(connection => connection.RequestDefinitionAsync(filePath, line, character, cancellationToken), (IReadOnlyList<SearchMatch>)[], cancellationToken);

    public Task<IReadOnlyList<SearchMatch>> GetReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => RequestAsync(connection => connection.RequestReferencesAsync(filePath, line, character, cancellationToken), (IReadOnlyList<SearchMatch>)[], cancellationToken);

    public Task<IReadOnlyList<LspFileEdits>?> RenameSymbolAsync(string filePath, int line, int character, string newName, CancellationToken cancellationToken = default)
        => RequestAsync(connection => connection.RequestRenameAsync(filePath, line, character, newName, cancellationToken), (IReadOnlyList<LspFileEdits>?)null, cancellationToken);

    public Task<IReadOnlyList<LspRangeEdit>?> FormatDocumentAsync(string filePath, int tabSize, CancellationToken cancellationToken = default)
        => RequestAsync(connection => connection.RequestFormattingAsync(filePath, tabSize, insertSpaces: true, cancellationToken), (IReadOnlyList<LspRangeEdit>?)null, cancellationToken);

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        KillProcess();
        _gate.Dispose();
    }

    /// <summary>
    /// Runs a request against the live connection: takes it under the gate, then does
    /// the round-trip outside the gate so requests never block document-sync
    /// notifications. Returns <paramref name="fallback"/> when no server is running or
    /// the request fails; never starts the server.
    /// </summary>
    private async Task<T> RequestAsync<T>(
        Func<LspServerConnection, Task<T>> request, T fallback, CancellationToken cancellationToken)
    {
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
            return fallback;
        }

        try
        {
            return await request(connection).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsServerFailure(ex) || ex is OperationCanceledException)
        {
            // A failed request is left for the notification paths to tear down.
            return fallback;
        }
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

        var command = _descriptor.GetCommand(_settingsService);
        var rootPath = _rootPath ?? Path.GetDirectoryName(fallbackPath) ?? Environment.CurrentDirectory;

        var process = StartProcess(command, rootPath);
        if (process is null)
        {
            _startFailed = true;
            _report($"{_descriptor.DisplayName} language services unavailable: '{command}' was not found. {_descriptor.InstallHint}");
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
                _descriptor.DiagnosticSource,
                _onDiagnostics,
                timeout.Token).ConfigureAwait(false);
            _process = process;
            _report($"{_descriptor.DisplayName} language server ready.");
            return _connection;
        }
        catch (Exception ex) when (ex is OperationCanceledException || IsServerFailure(ex))
        {
            _startFailed = true;
            _logger.LogError(ex, "Language server '{Command}' failed to initialize", command);
            _report($"{_descriptor.DisplayName} language server failed to start: {ex.Message}");
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
        _logger.LogWarning("{Server} language server exited unexpectedly", _descriptor.DisplayName);
        _report($"{_descriptor.DisplayName} language server exited unexpectedly.");
    }

    private void DrainStandardError(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    _logger.LogDebug("LSP stderr ({Server}): {Line}", _descriptor.DiagnosticSource, line);
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
        _logger.LogWarning(ex, "{Server} language server communication failed", _descriptor.DisplayName);
        _connection?.Dispose();
        _connection = null;
        KillProcess();
        _startFailed = true;
        _report($"{_descriptor.DisplayName} language server connection lost.");
    }

    private static bool IsServerFailure(Exception ex)
        => ex is RemoteRpcException or IOException or ObjectDisposedException or InvalidOperationException;
}
