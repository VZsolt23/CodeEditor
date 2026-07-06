using System.Collections.Specialized;
using System.Windows.Threading;
using CodeEditor.Application.Interfaces;
using CodeEditor.Application.Services;
using CodeEditor.Core.Diagnostics;
using CodeEditor.ViewModels;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Services;

/// <summary>
/// Glue between the editor and LSP-backed languages (TypeScript/JavaScript):
/// forwards document open/change/close to <see cref="ILspService"/> (debounced),
/// and routes the server's pushed diagnostics into editor squiggles and the
/// Problems panel under the "typescript" source. Must be constructed on the UI
/// thread (owns a <see cref="DispatcherTimer"/>).
/// </summary>
public sealed class LspDiagnosticsCoordinator
{
    private const string DiagnosticSource = "typescript";
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly ILspService _lspService;
    private readonly IWorkspaceService _workspaceService;
    private readonly DocumentsViewModel _documents;
    private readonly ProblemsViewModel _problems;
    private readonly ILogger<LspDiagnosticsCoordinator> _logger;
    private readonly DispatcherTimer _debounceTimer;
    private readonly HashSet<DocumentViewModel> _dirtyDocuments = [];
    private readonly Dictionary<DocumentViewModel, EventHandler<DocumentChangeEventArgs>> _textChangedHandlers = [];
    private readonly Dictionary<string, IReadOnlyList<DiagnosticItem>> _diagnosticsByPath = new(StringComparer.OrdinalIgnoreCase);

    public LspDiagnosticsCoordinator(
        ILspService lspService,
        IWorkspaceService workspaceService,
        DocumentsViewModel documents,
        ProblemsViewModel problems,
        ILogger<LspDiagnosticsCoordinator> logger)
    {
        _lspService = lspService;
        _workspaceService = workspaceService;
        _documents = documents;
        _problems = problems;
        _logger = logger;

        _debounceTimer = new DispatcherTimer { Interval = DebounceInterval };
        _debounceTimer.Tick += async (_, _) => await FlushChangesAsync();

        lspService.DiagnosticsPublished += OnDiagnosticsPublished;
        workspaceService.WorkspaceChanged += OnWorkspaceChanged;
        documents.Documents.CollectionChanged += OnDocumentsCollectionChanged;
        foreach (var document in documents.Documents)
        {
            Attach(document);
        }
    }

    private static bool IsLspDocument(DocumentViewModel document)
        => document.FilePath is not null && LspLanguages.Includes(document.Language.Id);

    private void OnWorkspaceChanged(object? sender, EventArgs e) => _ = ResetWorkspaceAsync();

    private async Task ResetWorkspaceAsync()
    {
        _diagnosticsByPath.Clear();
        PushProblems();

        await _lspService.SetWorkspaceAsync(_workspaceService.RootPath);

        // Announce already-open documents to the (lazily started) new server.
        foreach (var document in _documents.Documents.Where(IsLspDocument))
        {
            await OpenOnServerAsync(document);
        }
    }

    private void OnDocumentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var document in e.OldItems?.OfType<DocumentViewModel>() ?? [])
        {
            Detach(document);
        }

        foreach (var document in e.NewItems?.OfType<DocumentViewModel>() ?? [])
        {
            Attach(document);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var document in _textChangedHandlers.Keys.Except(_documents.Documents).ToList())
            {
                Detach(document);
            }
        }
    }

    private void Attach(DocumentViewModel document)
    {
        if (_textChangedHandlers.ContainsKey(document))
        {
            return;
        }

        EventHandler<DocumentChangeEventArgs> handler = (_, _) => MarkDirty(document);
        _textChangedHandlers[document] = handler;
        document.Document.Changed += handler;

        if (IsLspDocument(document))
        {
            _ = OpenOnServerAsync(document);
        }
    }

    private void Detach(DocumentViewModel document)
    {
        if (_textChangedHandlers.Remove(document, out var handler))
        {
            document.Document.Changed -= handler;
        }

        _dirtyDocuments.Remove(document);

        if (document.FilePath is { } filePath && IsLspDocument(document))
        {
            _ = CloseOnServerAsync(filePath);
            if (_diagnosticsByPath.Remove(filePath))
            {
                PushProblems();
            }
        }
    }

    private async Task OpenOnServerAsync(DocumentViewModel document)
    {
        if (document.FilePath is not { } filePath)
        {
            return;
        }

        try
        {
            await _lspService.NotifyDocumentOpenedAsync(filePath, document.Language.Id, document.Document.Text);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CloseOnServerAsync(string filePath)
    {
        try
        {
            await _lspService.NotifyDocumentClosedAsync(filePath);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void MarkDirty(DocumentViewModel document)
    {
        if (!IsLspDocument(document) || !_textChangedHandlers.ContainsKey(document))
        {
            return;
        }

        _dirtyDocuments.Add(document);
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task FlushChangesAsync()
    {
        _debounceTimer.Stop();
        var batch = _dirtyDocuments.ToList();
        _dirtyDocuments.Clear();

        foreach (var document in batch)
        {
            if (document.FilePath is not { } filePath || !_textChangedHandlers.ContainsKey(document))
            {
                continue;
            }

            try
            {
                await _lspService.NotifyDocumentChangedAsync(filePath, document.Document.Text);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void OnDiagnosticsPublished(object? sender, LspDiagnosticsEvent e)
    {
        // Server pushes on RPC threads; VM updates belong on the UI thread.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyDiagnostics(e);
        }
        else
        {
            dispatcher.BeginInvoke(() => ApplyDiagnostics(e));
        }
    }

    private void ApplyDiagnostics(LspDiagnosticsEvent e)
    {
        var document = _documents.Documents.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, e.FilePath, StringComparison.OrdinalIgnoreCase));

        if (document is null || !IsLspDocument(document))
        {
            // Late publish for a closed tab; nothing to show.
            _diagnosticsByPath.Remove(e.FilePath);
            PushProblems();
            return;
        }

        _logger.LogDebug("LSP published {Count} diagnostics for {Path}", e.Diagnostics.Count, e.FilePath);
        document.Diagnostics = e.Diagnostics.Count > 0 ? e.Diagnostics : null;

        if (e.Diagnostics.Count > 0)
        {
            _diagnosticsByPath[e.FilePath] = e.Diagnostics;
        }
        else
        {
            _diagnosticsByPath.Remove(e.FilePath);
        }

        PushProblems();
    }

    private void PushProblems()
        => _problems.SetDiagnostics(DiagnosticSource, [.. _diagnosticsByPath.Values.SelectMany(items => items)]);
}
