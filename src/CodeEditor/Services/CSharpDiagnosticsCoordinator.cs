using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Diagnostics;
using CodeEditor.ViewModels;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Services;

/// <summary>
/// Glue between the editor and C# analysis: loads/unloads the Roslyn workspace
/// with the folder workspace, watches open C# documents, and pushes debounced
/// per-document diagnostics into the Problems panel under the "csharp" source.
/// Must be constructed on the UI thread (owns a <see cref="DispatcherTimer"/>).
/// </summary>
public sealed class CSharpDiagnosticsCoordinator
{
    private const string DiagnosticSource = "csharp";
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(700);

    private readonly ICodeAnalysisService _codeAnalysis;
    private readonly IWorkspaceService _workspaceService;
    private readonly DocumentsViewModel _documents;
    private readonly ProblemsViewModel _problems;
    private readonly ILogger<CSharpDiagnosticsCoordinator> _logger;
    private readonly DispatcherTimer _debounceTimer;
    private readonly HashSet<DocumentViewModel> _dirtyDocuments = [];
    private readonly Dictionary<DocumentViewModel, EventHandler<DocumentChangeEventArgs>> _textChangedHandlers = [];
    private readonly Dictionary<DocumentViewModel, IReadOnlyList<DiagnosticItem>> _results = [];

    private CancellationTokenSource? _loadCts;
    private bool _isProcessing;

    public CSharpDiagnosticsCoordinator(
        ICodeAnalysisService codeAnalysis,
        IWorkspaceService workspaceService,
        DocumentsViewModel documents,
        ProblemsViewModel problems,
        ILogger<CSharpDiagnosticsCoordinator> logger)
    {
        _codeAnalysis = codeAnalysis;
        _workspaceService = workspaceService;
        _documents = documents;
        _problems = problems;
        _logger = logger;

        _debounceTimer = new DispatcherTimer { Interval = DebounceInterval };
        _debounceTimer.Tick += async (_, _) => await ProcessDirtyDocumentsAsync();

        workspaceService.WorkspaceChanged += OnWorkspaceChanged;
        documents.Documents.CollectionChanged += OnDocumentsCollectionChanged;
        foreach (var document in documents.Documents)
        {
            Attach(document);
        }
    }

    private static bool IsAnalyzable(DocumentViewModel document)
        => document.FilePath is not null && document.Language.Id == "csharp";

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        _loadCts?.Cancel();

        if (_workspaceService.RootPath is { } rootPath)
        {
            var cts = new CancellationTokenSource();
            _loadCts = cts;
            _ = LoadWorkspaceAsync(rootPath, cts.Token);
        }
        else
        {
            _codeAnalysis.Unload();
            _results.Clear();
            _problems.SetDiagnostics(DiagnosticSource, []);
        }
    }

    private async Task LoadWorkspaceAsync(string rootPath, CancellationToken cancellationToken)
    {
        try
        {
            await _codeAnalysis.LoadAsync(rootPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // Re-analyze everything that is open now that semantics are available.
        foreach (var document in _documents.Documents.Where(IsAnalyzable))
        {
            _dirtyDocuments.Add(document);
        }

        RestartDebounce();
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

        PushProblems();
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
        document.PropertyChanged += OnDocumentPropertyChanged;

        MarkDirty(document);
    }

    private void Detach(DocumentViewModel document)
    {
        if (_textChangedHandlers.Remove(document, out var handler))
        {
            document.Document.Changed -= handler;
        }

        document.PropertyChanged -= OnDocumentPropertyChanged;
        _dirtyDocuments.Remove(document);
        _results.Remove(document);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Save As / rename can change what (and whether) we analyze.
        if (sender is DocumentViewModel document
            && e.PropertyName is nameof(DocumentViewModel.FilePath) or nameof(DocumentViewModel.Language))
        {
            MarkDirty(document);
        }
    }

    private void MarkDirty(DocumentViewModel document)
    {
        if (!_textChangedHandlers.ContainsKey(document))
        {
            return;
        }

        _dirtyDocuments.Add(document);
        RestartDebounce();
    }

    private void RestartDebounce()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task ProcessDirtyDocumentsAsync()
    {
        _debounceTimer.Stop();
        if (_isProcessing)
        {
            return;
        }

        _isProcessing = true;
        try
        {
            var batch = _dirtyDocuments.ToList();
            _dirtyDocuments.Clear();

            foreach (var document in batch)
            {
                if (!_textChangedHandlers.ContainsKey(document))
                {
                    continue;
                }

                if (!IsAnalyzable(document))
                {
                    _results.Remove(document);
                    document.Diagnostics = null;
                    document.SemanticHighlights = null;
                    continue;
                }

                var filePath = document.FilePath!;
                var text = document.Document.Text;
                try
                {
                    var diagnostics = await _codeAnalysis.GetDiagnosticsAsync(filePath, text);
                    _results[document] = diagnostics;
                    document.Diagnostics = diagnostics;
                    document.SemanticHighlights = await _codeAnalysis.GetClassificationsAsync(filePath, text);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    _logger.LogWarning(ex, "Diagnostics failed for {Path}", filePath);
                }
            }

            PushProblems();
        }
        finally
        {
            _isProcessing = false;
            if (_dirtyDocuments.Count > 0)
            {
                _debounceTimer.Start();
            }
        }
    }

    private void PushProblems()
    {
        var diagnostics = _results
            .Where(pair => _textChangedHandlers.ContainsKey(pair.Key) && IsAnalyzable(pair.Key))
            .SelectMany(pair => pair.Value)
            .ToList();
        _problems.SetDiagnostics(DiagnosticSource, diagnostics);
    }
}
