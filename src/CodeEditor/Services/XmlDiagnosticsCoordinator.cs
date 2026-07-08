using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using CodeEditor.Core.Diagnostics;
using CodeEditor.ViewModels;
using ICSharpCode.AvalonEdit.Document;

namespace CodeEditor.Services;

/// <summary>
/// Watches open XML documents and pushes debounced well-formedness diagnostics
/// (<see cref="XmlWellFormednessChecker"/>) into squiggles and the Problems panel
/// under the "xml" source. Same shape as <see cref="CSharpDiagnosticsCoordinator"/>
/// but with no workspace to load and a synchronous, in-process checker.
/// Must be constructed on the UI thread (owns a <see cref="DispatcherTimer"/>).
/// </summary>
public sealed class XmlDiagnosticsCoordinator
{
    private const string DiagnosticSource = "xml";
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly DocumentsViewModel _documents;
    private readonly ProblemsViewModel _problems;
    private readonly DispatcherTimer _debounceTimer;
    private readonly HashSet<DocumentViewModel> _dirtyDocuments = [];
    private readonly Dictionary<DocumentViewModel, EventHandler<DocumentChangeEventArgs>> _textChangedHandlers = [];
    private readonly Dictionary<DocumentViewModel, IReadOnlyList<DiagnosticItem>> _results = [];

    public XmlDiagnosticsCoordinator(DocumentsViewModel documents, ProblemsViewModel problems)
    {
        _documents = documents;
        _problems = problems;

        _debounceTimer = new DispatcherTimer { Interval = DebounceInterval };
        _debounceTimer.Tick += (_, _) => ProcessDirtyDocuments();

        documents.Documents.CollectionChanged += OnDocumentsCollectionChanged;
        foreach (var document in documents.Documents)
        {
            Attach(document);
        }
    }

    private static bool IsCheckable(DocumentViewModel document) => document.Language.Id == "xml";

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
        // Save As / rename can change whether the document is XML at all.
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
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ProcessDirtyDocuments()
    {
        _debounceTimer.Stop();

        foreach (var document in _dirtyDocuments.ToList())
        {
            if (!_textChangedHandlers.ContainsKey(document))
            {
                continue;
            }

            if (!IsCheckable(document))
            {
                if (_results.Remove(document))
                {
                    document.Diagnostics = null;
                }

                continue;
            }

            var diagnostics = XmlWellFormednessChecker.Check(document.FilePath, document.Document.Text);
            _results[document] = diagnostics;
            document.Diagnostics = diagnostics;
        }

        _dirtyDocuments.Clear();
        PushProblems();
    }

    private void PushProblems()
    {
        var diagnostics = _results
            .Where(pair => _textChangedHandlers.ContainsKey(pair.Key) && IsCheckable(pair.Key))
            .SelectMany(pair => pair.Value)
            .ToList();
        _problems.SetDiagnostics(DiagnosticSource, diagnostics);
    }
}
