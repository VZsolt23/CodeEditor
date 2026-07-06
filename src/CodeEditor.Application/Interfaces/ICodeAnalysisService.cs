using CodeEditor.Core.Completion;
using CodeEditor.Core.Diagnostics;
using CodeEditor.Core.Documents;
using CodeEditor.Core.Workspace;

namespace CodeEditor.Application.Interfaces;

/// <summary>
/// C# language analysis over the opened workspace (Roslyn-backed). Loading is
/// best-effort: when no solution/projects load, per-file analysis degrades to
/// syntax-only diagnostics. Thread-safe; events may fire on any thread.
/// </summary>
public interface ICodeAnalysisService
{
    /// <summary>Whether a solution or projects are currently loaded.</summary>
    bool IsLoaded { get; }

    /// <summary>Human-readable status updates ("Loading C# workspace…", "C# ready: 4 projects"). May fire on any thread.</summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Finds and loads the solution (.sln/.slnx) or projects under
    /// <paramref name="rootPath"/>. Failures are reported via
    /// <see cref="StatusChanged"/> and logging; the method does not throw for them.
    /// </summary>
    Task LoadAsync(string rootPath, CancellationToken cancellationToken = default);

    /// <summary>Releases the loaded workspace, if any.</summary>
    void Unload();

    /// <summary>
    /// Computes diagnostics for one file given its current (possibly unsaved) text.
    /// Files belonging to the loaded workspace get full semantic diagnostics —
    /// the updated text is kept, so cross-file edits accumulate; other files get
    /// syntax-only diagnostics.
    /// </summary>
    Task<IReadOnlyList<DiagnosticItem>> GetDiagnosticsAsync(
        string filePath, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes completions at <paramref name="offset"/> in the given (possibly
    /// unsaved) text. Returns null when the file is not part of the loaded
    /// workspace or nothing applies. The result stays valid until the next
    /// completion request; commit items via <see cref="GetCompletionChangeAsync"/>.
    /// </summary>
    Task<CompletionResultInfo?> GetCompletionsAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the exact text change that commits item <paramref name="itemIndex"/>
    /// of the most recent completion list, or null when the list is gone.
    /// </summary>
    Task<CompletionChangeInfo?> GetCompletionChangeAsync(
        int itemIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns hover text (signature and documentation) for the symbol at
    /// <paramref name="offset"/>, or null when there is nothing to show.
    /// </summary>
    Task<string?> GetQuickInfoAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns overload help for the invocation the caret at <paramref name="offset"/>
    /// is inside, or null when the caret is not in an argument list.
    /// </summary>
    Task<SignatureHelpInfo?> GetSignatureHelpAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the source definition(s) of the symbol at <paramref name="offset"/>.
    /// Empty when there is no symbol or its definition lives in metadata.
    /// </summary>
    Task<IReadOnlyList<SearchMatch>> GetDefinitionsAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds all references (including definitions) of the symbol at
    /// <paramref name="offset"/>, sorted by file and line.
    /// </summary>
    Task<IReadOnlyList<SearchMatch>> FindReferencesAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies the whole document for semantic highlighting (sorted by offset).
    /// Empty when the file is not part of the loaded workspace.
    /// </summary>
    Task<IReadOnlyList<ClassifiedSpanInfo>> GetClassificationsAsync(
        string filePath, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the edits that format the whole document with the C# formatting
    /// conventions. Returns null when the file is not part of the loaded
    /// workspace; an empty list when the document is already formatted.
    /// </summary>
    Task<IReadOnlyList<TextEditInfo>?> FormatDocumentAsync(
        string filePath, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the symbol at <paramref name="offset"/> to <paramref name="newName"/>
    /// across the workspace. Returns the changed files' full new contents, or null
    /// when there is no renamable source symbol or the name is invalid.
    /// </summary>
    Task<IReadOnlyList<FileTextChange>?> RenameSymbolAsync(
        string filePath, string text, int offset, string newName, CancellationToken cancellationToken = default);
}
