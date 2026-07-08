using System.IO;
using CodeEditor.Application.Interfaces;
using CodeEditor.Application.Services;
using CodeEditor.Core.Completion;
using CodeEditor.Core.Documents;
using CodeEditor.Core.Workspace;
using CodeEditor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

namespace CodeEditor.ViewModels;

/// <summary>
/// A caret/selection target inside a document (1-based line and column).
/// <paramref name="FocusEditor"/> is false for navigations that must not steal
/// keyboard focus (e.g. cycling matches while typing in the find panel).
/// </summary>
public sealed record DocumentNavigation(int Line, int Column, int SelectionLength, bool FocusEditor = true);

/// <summary>
/// Represents a single open document (one editor tab): its text buffer,
/// file binding, dirty state, language, and caret position.
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject
{
    private readonly string _untitledName;
    private readonly ICodeAnalysisService _codeAnalysis;
    private readonly ILspService _lspService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string? _filePath;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Blocks user edits in the editor. Set at open for files with the read-only
    /// attribute; toggleable per document from the Edit menu. A UI-level guard —
    /// programmatic buffer edits (e.g. multi-file rename) are not affected.
    /// </summary>
    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    [ObservableProperty]
    private LanguageInfo _language;

    /// <summary>
    /// Encoding the document is saved in: detected when the file was opened,
    /// UTF-8 (no BOM) for new documents, changeable via the status bar pill.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EncodingDisplayName))]
    private TextEncodingKind _encoding = TextEncodingKind.Utf8;

    [ObservableProperty]
    private IHighlightingDefinition? _syntaxHighlighting;

    /// <summary>
    /// Pending caret/selection request, consumed (and cleared) by the editor view.
    /// A property rather than an event so requests issued before the shared editor
    /// binds to this document are not lost.
    /// </summary>
    [ObservableProperty]
    private DocumentNavigation? _pendingNavigation;

    /// <summary>Current editor selection text, kept up to date by the editor view.</summary>
    [ObservableProperty]
    private string _selectedText = string.Empty;

    /// <summary>Language-service diagnostics for this document (drives editor squiggles), or null for none.</summary>
    [ObservableProperty]
    private IReadOnlyList<CodeEditor.Core.Diagnostics.DiagnosticItem>? _diagnostics;

    /// <summary>Semantic classification spans (drives the editor's semantic colors), or null for none.</summary>
    [ObservableProperty]
    private IReadOnlyList<ClassifiedSpanInfo>? _semanticHighlights;

    /// <summary>Find/replace matches to highlight in the editor, or null for none.</summary>
    [ObservableProperty]
    private IReadOnlyList<TextSpan>? _searchHighlights;

    /// <summary>The find/replace match the caret is on, drawn with a stronger brush.</summary>
    [ObservableProperty]
    private TextSpan? _currentSearchHighlight;

    /// <param name="filePath">Backing file, or null for an untitled document.</param>
    /// <param name="initialText">Initial buffer content.</param>
    /// <param name="untitledName">Display name used while the document has no file.</param>
    /// <param name="language">Language resolved for the document.</param>
    /// <param name="options">Shared editor display options.</param>
    /// <param name="codeAnalysis">C# (Roslyn) language services.</param>
    /// <param name="lspService">LSP language services (TypeScript/JavaScript).</param>
    /// <param name="closeRequested">Callback invoked when the user asks to close this document.</param>
    public DocumentViewModel(
        string? filePath,
        string initialText,
        string untitledName,
        LanguageInfo language,
        EditorOptionsViewModel options,
        ICodeAnalysisService codeAnalysis,
        ILspService lspService,
        Func<DocumentViewModel, Task> closeRequested)
    {
        ArgumentNullException.ThrowIfNull(closeRequested);

        _codeAnalysis = codeAnalysis;
        _lspService = lspService;
        _filePath = filePath;
        _untitledName = untitledName;
        _language = language;
        Options = options;
        Document = new TextDocument(initialText);
        _syntaxHighlighting = EditorHighlighting.GetDefinition(filePath);
        CloseCommand = new AsyncRelayCommand(() => closeRequested(this));

        Document.Changed += (_, _) => IsDirty = true;
    }

    /// <summary>The AvalonEdit text buffer. Owns the undo stack, so it survives tab switches.</summary>
    public TextDocument Document { get; }

    /// <summary>Shared editor display options (font, wrap, tabs).</summary>
    public EditorOptionsViewModel Options { get; }

    /// <summary>Closes this document, prompting for unsaved changes.</summary>
    public IAsyncRelayCommand CloseCommand { get; }

    /// <summary>Display name: the file name, or the untitled placeholder.</summary>
    public string FileName => FilePath is null ? _untitledName : Path.GetFileName(FilePath);

    /// <summary>Status bar text for <see cref="Encoding"/> (e.g. "UTF-8", "UTF-16 LE").</summary>
    public string EncodingDisplayName => Encoding.DisplayName();

    /// <summary>
    /// Requests code completion at <paramref name="offset"/>: Roslyn for C#, the
    /// language server for TypeScript/JavaScript, null otherwise.
    /// </summary>
    public async Task<CompletionResultInfo?> GetCompletionsAsync(int offset, CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return null;
        }

        if (Language.Id == "csharp")
        {
            return await _codeAnalysis.GetCompletionsAsync(FilePath, Document.Text, offset, cancellationToken);
        }

        if (!LspLanguages.Includes(Language.Id))
        {
            return null;
        }

        // Make sure the server has the current buffer before it computes completions.
        var text = Document.Text;
        await _lspService.NotifyDocumentChangedAsync(FilePath, text, cancellationToken);

        var location = Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength));
        var items = await _lspService.GetCompletionsAsync(FilePath, location.Line - 1, location.Column - 1, cancellationToken);
        if (items is null || items.Count == 0)
        {
            return null;
        }

        // LSP replaces the already-typed word; compute that span from the buffer.
        var wordStart = FindWordStart(text, offset);
        return new CompletionResultInfo(wordStart, offset - wordStart, items);
    }

    private static int FindWordStart(string text, int offset)
    {
        var start = Math.Clamp(offset, 0, text.Length);
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        return start;

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
    }

    /// <summary>Returns the text change committing an item of the last completion list.</summary>
    public Task<CompletionChangeInfo?> GetCompletionChangeAsync(int itemIndex, CancellationToken cancellationToken = default)
        => _codeAnalysis.GetCompletionChangeAsync(itemIndex, cancellationToken);

    /// <summary>
    /// Hover text for the symbol at <paramref name="offset"/>: Roslyn for C#, the
    /// language server for TypeScript/JavaScript, null otherwise.
    /// </summary>
    public Task<string?> GetQuickInfoAsync(int offset, CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return Task.FromResult<string?>(null);
        }

        if (Language.Id == "csharp")
        {
            return _codeAnalysis.GetQuickInfoAsync(FilePath, Document.Text, offset, cancellationToken);
        }

        if (LspLanguages.Includes(Language.Id))
        {
            // LSP positions are 0-based; TextDocument.GetLocation is 1-based line/column.
            var location = Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength));
            return _lspService.GetHoverAsync(FilePath, location.Line - 1, location.Column - 1, cancellationToken);
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>Overload help for the call at <paramref name="offset"/> (null for non-C# documents).</summary>
    public Task<SignatureHelpInfo?> GetSignatureHelpAsync(int offset, CancellationToken cancellationToken = default)
        => FilePath is null || Language.Id != "csharp"
            ? Task.FromResult<SignatureHelpInfo?>(null)
            : _codeAnalysis.GetSignatureHelpAsync(FilePath, Document.Text, offset, cancellationToken);

    /// <summary>
    /// Source definitions of the symbol at <paramref name="offset"/>: Roslyn for
    /// C#, the language server for TypeScript/JavaScript, empty otherwise.
    /// </summary>
    public async Task<IReadOnlyList<SearchMatch>> GetDefinitionsAsync(int offset, CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return [];
        }

        if (Language.Id == "csharp")
        {
            return await _codeAnalysis.GetDefinitionsAsync(FilePath, Document.Text, offset, cancellationToken);
        }

        if (!LspLanguages.Includes(Language.Id))
        {
            return [];
        }

        await _lspService.NotifyDocumentChangedAsync(FilePath, Document.Text, cancellationToken);
        var location = Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength));
        return await _lspService.GetDefinitionsAsync(FilePath, location.Line - 1, location.Column - 1, cancellationToken);
    }

    /// <summary>
    /// All references to the symbol at <paramref name="offset"/>: Roslyn for C#, the
    /// language server for TypeScript/JavaScript, empty otherwise.
    /// </summary>
    public async Task<IReadOnlyList<SearchMatch>> GetReferencesAsync(int offset, CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return [];
        }

        if (Language.Id == "csharp")
        {
            return await _codeAnalysis.FindReferencesAsync(FilePath, Document.Text, offset, cancellationToken);
        }

        if (!LspLanguages.Includes(Language.Id))
        {
            return [];
        }

        await _lspService.NotifyDocumentChangedAsync(FilePath, Document.Text, cancellationToken);
        var location = Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength));
        return await _lspService.GetReferencesAsync(FilePath, location.Line - 1, location.Column - 1, cancellationToken);
    }

    /// <summary>
    /// Format edits for the whole document: Roslyn for C# (offset-based), the language
    /// server for TS/JS (LSP range edits converted to offsets), null otherwise.
    /// </summary>
    public async Task<IReadOnlyList<TextEditInfo>?> GetFormattingEditsAsync(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return null;
        }

        if (Language.Id == "csharp")
        {
            return await _codeAnalysis.FormatDocumentAsync(FilePath, Document.Text, cancellationToken);
        }

        if (!LspLanguages.Includes(Language.Id))
        {
            return null;
        }

        await _lspService.NotifyDocumentChangedAsync(FilePath, Document.Text, cancellationToken);
        var rangeEdits = await _lspService.FormatDocumentAsync(FilePath, Options.TabWidth, cancellationToken);
        if (rangeEdits is null)
        {
            return null;
        }

        var edits = new List<TextEditInfo>(rangeEdits.Count);
        foreach (var edit in rangeEdits)
        {
            var start = ToOffset(edit.StartLine, edit.StartChar);
            var end = Math.Max(start, ToOffset(edit.EndLine, edit.EndChar));
            edits.Add(new TextEditInfo(start, end - start, edit.NewText));
        }

        return edits;
    }

    /// <summary>Converts a 0-based LSP line/character position to a document offset, clamped to bounds.</summary>
    private int ToOffset(int lspLine, int lspChar)
    {
        var lineNumber = Math.Clamp(lspLine + 1, 1, Document.LineCount);
        var line = Document.GetLineByNumber(lineNumber);
        return line.Offset + Math.Clamp(lspChar, 0, line.Length);
    }

    /// <summary>Requests the editor to move the caret and select <paramref name="selectionLength"/> characters.</summary>
    public void NavigateTo(int line, int column, int selectionLength, bool focusEditor = true)
        => PendingNavigation = new DocumentNavigation(line, column, selectionLength, focusEditor);

    /// <summary>Updates the caret position shown in the status bar.</summary>
    public void UpdateCaret(int line, int column)
    {
        CaretLine = line;
        CaretColumn = column;
    }

    /// <summary>Binds the document to a file (after Save As) and refreshes language services.</summary>
    public void SetFile(string filePath, LanguageInfo language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = filePath;
        Language = language;
        SyntaxHighlighting = EditorHighlighting.GetDefinition(filePath);
    }

    /// <summary>Clears the dirty flag after a successful save.</summary>
    public void MarkSaved() => IsDirty = false;
}
