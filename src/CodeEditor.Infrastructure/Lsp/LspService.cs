using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Completion;
using CodeEditor.Core.Diagnostics;
using CodeEditor.Core.Documents;
using CodeEditor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Infrastructure.Lsp;

/// <summary>
/// Routes editor requests to per-language <see cref="LspServerHost"/>s (each hosting
/// one server process). A document is bound to a host by its language on open; later
/// notifications and requests for that file route to the same host. All operations
/// are best-effort — a missing server or a dead process reports via
/// <see cref="StatusChanged"/> instead of throwing.
/// </summary>
public sealed class LspService : ILspService, IDisposable
{
    private static readonly IReadOnlyList<LspServerDescriptor> Descriptors =
    [
        new LspServerDescriptor(
            DisplayName: "TypeScript/JavaScript",
            DiagnosticSource: "typescript",
            Languages: new HashSet<string>(StringComparer.Ordinal)
            {
                "typescript",
                "typescriptreact",
                "javascript",
                "javascriptreact",
            },
            GetCommand: settings => settings.Settings.TypeScriptServerCommand,
            InstallHint: "Install it with: npm install -g typescript-language-server typescript"),
        new LspServerDescriptor(
            DisplayName: "HTML",
            DiagnosticSource: "html",
            Languages: new HashSet<string>(StringComparer.Ordinal) { "html" },
            GetCommand: settings => settings.Settings.HtmlServerCommand,
            InstallHint: "Install it with: npm install -g vscode-langservers-extracted"),
        new LspServerDescriptor(
            DisplayName: "CSS",
            DiagnosticSource: "css",
            Languages: new HashSet<string>(StringComparer.Ordinal) { "css", "scss", "less" },
            GetCommand: settings => settings.Settings.CssServerCommand,
            InstallHint: "Install it with: npm install -g vscode-langservers-extracted"),
        new LspServerDescriptor(
            DisplayName: "JSON",
            DiagnosticSource: "json",
            Languages: new HashSet<string>(StringComparer.Ordinal) { "json", "jsonc" },
            GetCommand: settings => settings.Settings.JsonServerCommand,
            InstallHint: "Install it with: npm install -g vscode-langservers-extracted"),
    ];

    private readonly ILogger<LspService> _logger;
    private readonly List<LspServerHost> _hosts;
    private readonly Dictionary<string, LspServerHost> _hostByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _mapLock = new();

    public LspService(ISettingsService settingsService, ILogger<LspService> logger)
    {
        _logger = logger;
        _hosts = [.. Descriptors.Select(descriptor => new LspServerHost(
            descriptor,
            settingsService,
            logger,
            RaiseDiagnostics,
            Report))];
    }

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<LspDiagnosticsEvent>? DiagnosticsPublished;

    public async Task SetWorkspaceAsync(string? rootPath, CancellationToken cancellationToken = default)
    {
        lock (_mapLock)
        {
            _hostByFile.Clear();
        }

        foreach (var host in _hosts)
        {
            await host.SetWorkspaceAsync(rootPath, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NotifyDocumentOpenedAsync(
        string filePath, string languageId, string text, CancellationToken cancellationToken = default)
    {
        var host = _hosts.FirstOrDefault(candidate => candidate.Handles(languageId));
        if (host is null)
        {
            return;
        }

        lock (_mapLock)
        {
            _hostByFile[filePath] = host;
        }

        await host.OpenDocumentAsync(filePath, languageId, text, cancellationToken).ConfigureAwait(false);
    }

    public async Task NotifyDocumentChangedAsync(string filePath, string text, CancellationToken cancellationToken = default)
    {
        if (HostForFile(filePath) is { } host)
        {
            await host.ChangeDocumentAsync(filePath, text, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NotifyDocumentClosedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        LspServerHost? host;
        lock (_mapLock)
        {
            _hostByFile.Remove(filePath, out host);
        }

        if (host is not null)
        {
            await host.CloseDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<string?> GetHoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => HostForFile(filePath) is { } host
            ? host.GetHoverAsync(filePath, line, character, cancellationToken)
            : Task.FromResult<string?>(null);

    public Task<IReadOnlyList<CompletionItemInfo>?> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => HostForFile(filePath) is { } host
            ? host.GetCompletionsAsync(filePath, line, character, cancellationToken)
            : Task.FromResult<IReadOnlyList<CompletionItemInfo>?>(null);

    public Task<IReadOnlyList<SearchMatch>> GetDefinitionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => HostForFile(filePath) is { } host
            ? host.GetDefinitionsAsync(filePath, line, character, cancellationToken)
            : Task.FromResult<IReadOnlyList<SearchMatch>>([]);

    public Task<IReadOnlyList<SearchMatch>> GetReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
        => HostForFile(filePath) is { } host
            ? host.GetReferencesAsync(filePath, line, character, cancellationToken)
            : Task.FromResult<IReadOnlyList<SearchMatch>>([]);

    public Task<IReadOnlyList<LspFileEdits>?> RenameSymbolAsync(string filePath, int line, int character, string newName, CancellationToken cancellationToken = default)
        => HostForFile(filePath) is { } host
            ? host.RenameSymbolAsync(filePath, line, character, newName, cancellationToken)
            : Task.FromResult<IReadOnlyList<LspFileEdits>?>(null);

    public Task<IReadOnlyList<LspRangeEdit>?> FormatDocumentAsync(string filePath, int tabSize, CancellationToken cancellationToken = default)
        => HostForFile(filePath) is { } host
            ? host.FormatDocumentAsync(filePath, tabSize, cancellationToken)
            : Task.FromResult<IReadOnlyList<LspRangeEdit>?>(null);

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }

    private LspServerHost? HostForFile(string filePath)
    {
        lock (_mapLock)
        {
            return _hostByFile.GetValueOrDefault(filePath);
        }
    }

    private void RaiseDiagnostics(string filePath, IReadOnlyList<DiagnosticItem> diagnostics)
        => DiagnosticsPublished?.Invoke(this, new LspDiagnosticsEvent(filePath, diagnostics));

    private void Report(string status)
    {
        _logger.LogInformation("{Status}", status);
        StatusChanged?.Invoke(this, status);
    }
}
