using CodeEditor.Core.Diagnostics;

namespace CodeEditor.Application.Interfaces;

/// <summary>Diagnostics pushed by a language server for one file.</summary>
public sealed record LspDiagnosticsEvent(string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics);

/// <summary>
/// LSP-backed language services (TypeScript/JavaScript first). Unlike Roslyn,
/// diagnostics are pushed by the server after document notifications. All
/// methods are best-effort and never throw for server problems — failures are
/// logged and reported via <see cref="StatusChanged"/>. Events may fire on any thread.
/// </summary>
public interface ILspService
{
    /// <summary>Human-readable status updates (server ready / unavailable). May fire on any thread.</summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>Raised when the server publishes diagnostics for a file. May fire on any thread.</summary>
    event EventHandler<LspDiagnosticsEvent>? DiagnosticsPublished;

    /// <summary>
    /// Sets (or clears, with null) the workspace root, stopping any running server.
    /// The server restarts lazily on the next document notification.
    /// </summary>
    Task SetWorkspaceAsync(string? rootPath, CancellationToken cancellationToken = default);

    /// <summary>Announces an opened document; starts the server on first use.</summary>
    Task NotifyDocumentOpenedAsync(string filePath, string languageId, string text, CancellationToken cancellationToken = default);

    /// <summary>Sends the full new text of a changed document.</summary>
    Task NotifyDocumentChangedAsync(string filePath, string text, CancellationToken cancellationToken = default);

    /// <summary>Announces a closed document.</summary>
    Task NotifyDocumentClosedAsync(string filePath, CancellationToken cancellationToken = default);
}
