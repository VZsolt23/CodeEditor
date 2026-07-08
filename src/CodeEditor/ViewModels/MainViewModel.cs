using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CodeEditor.Application.Interfaces;
using CodeEditor.Application.Services;
using CodeEditor.Core.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeEditor.ViewModels;

/// <summary>
/// Root ViewModel of the main window. Composes the document manager and editor
/// options, and owns app-level concerns: title, theme selection, recent files,
/// panel visibility, and application exit.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string ApplicationName = "Code Editor";

    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IRecentFilesService _recentFilesService;
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly ICodeAnalysisService _codeAnalysisService;
    private readonly ILspService _lspService;

    private DocumentViewModel? _observedDocument;

    [ObservableProperty]
    private string _title = ApplicationName;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private bool _isBottomPanelVisible = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    public MainViewModel(
        DocumentsViewModel documents,
        ExplorerViewModel explorer,
        SearchViewModel search,
        FindReplaceViewModel find,
        OutputViewModel output,
        ProblemsViewModel problems,
        TerminalViewModel terminal,
        EditorOptionsViewModel options,
        IThemeService themeService,
        ISettingsService settingsService,
        IRecentFilesService recentFilesService,
        IFileService fileService,
        IDialogService dialogService,
        ICodeAnalysisService codeAnalysisService,
        ILspService lspService)
    {
        Documents = documents;
        Explorer = explorer;
        Search = search;
        Find = find;
        Output = output;
        Problems = problems;
        Terminal = terminal;
        Options = options;
        _themeService = themeService;
        _settingsService = settingsService;
        _recentFilesService = recentFilesService;
        _fileService = fileService;
        _dialogService = dialogService;
        _codeAnalysisService = codeAnalysisService;

        foreach (var theme in themeService.AvailableThemes)
        {
            ThemeOptions.Add(new ThemeOptionViewModel(theme, themeService)
            {
                IsSelected = theme.Id == themeService.CurrentTheme.Id,
            });
        }

        themeService.ThemeChanged += OnThemeChanged;
        codeAnalysisService.StatusChanged += OnCodeAnalysisStatusChanged;
        _lspService = lspService;
        lspService.StatusChanged += OnCodeAnalysisStatusChanged;
        recentFilesService.RecentFilesChanged += (_, _) => RebuildRecentFiles();
        settingsService.SettingsReloaded += OnSettingsReloaded;
        documents.PropertyChanged += OnDocumentsPropertyChanged;
        explorer.PropertyChanged += OnExplorerPropertyChanged;

        RebuildRecentFiles();
    }

    /// <summary>
    /// Applies an externally edited settings file at runtime: theme, editor display
    /// options, recent files, and the explorer's exclusion list. Raised by the
    /// watcher on a background thread, so the work is marshaled to the UI thread.
    /// </summary>
    private void OnSettingsReloaded(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var settings = _settingsService.Settings;
            if (!string.Equals(settings.Theme, _themeService.CurrentTheme.Id, StringComparison.OrdinalIgnoreCase))
            {
                _themeService.ApplyTheme(settings.Theme);
            }

            Options.ApplyFromSettings();
            RebuildRecentFiles();
            if (Explorer.HasWorkspace)
            {
                Explorer.RefreshCommand.Execute(null);
            }

            StatusText = "Settings reloaded from settings.json";
        });
    }

    /// <summary>Raised when the user requests application exit (File → Exit).</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised when the user requests Find in Files; the window focuses the search panel.</summary>
    public event EventHandler? FindInFilesRequested;

    /// <summary>Raised when results were placed in the search panel; the window brings it into view (without stealing focus).</summary>
    public event EventHandler? SearchResultsRequested;

    public DocumentsViewModel Documents { get; }

    public ExplorerViewModel Explorer { get; }

    public SearchViewModel Search { get; }

    public FindReplaceViewModel Find { get; }

    public OutputViewModel Output { get; }

    public ProblemsViewModel Problems { get; }

    public TerminalViewModel Terminal { get; }

    public EditorOptionsViewModel Options { get; }

    public ObservableCollection<ThemeOptionViewModel> ThemeOptions { get; } = [];

    public ObservableCollection<RecentFileViewModel> RecentFiles { get; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void FindInFiles()
    {
        IsSidebarVisible = true;
        FindInFilesRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task OpenSettingsFileAsync()
    {
        // Materialize the file first so a fresh install can still open it.
        if (!_fileService.FileExists(_settingsService.SettingsFilePath))
        {
            await _settingsService.SaveAsync();
        }

        await Documents.OpenAsync(_settingsService.SettingsFilePath);
    }

    /// <summary>Jumps to the definition of the symbol at the caret (F12); Roslyn for C#, LSP for TS/JS.</summary>
    [RelayCommand(CanExecute = nameof(CanUseSymbolServices))]
    private async Task GoToDefinitionAsync()
    {
        if (Documents.ActiveDocument is not { FilePath: not null } document
            || GetCaretOffset(document) is not { } offset)
        {
            return;
        }

        IReadOnlyList<SearchMatch> definitions;
        try
        {
            definitions = await document.GetDefinitionsAsync(offset);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        switch (definitions.Count)
        {
            case 0:
                StatusText = "No definition found (it may be in metadata).";
                break;
            case 1:
                await NavigateToMatchAsync(definitions[0]);
                break;
            default:
                ShowMatchesInSearchPanel($"{definitions.Count} definitions found.", definitions);
                break;
        }
    }

    /// <summary>Lists all references of the symbol at the caret in the search panel (Shift+F12); Roslyn for C#, LSP for TS/JS.</summary>
    [RelayCommand(CanExecute = nameof(CanUseSymbolServices))]
    private async Task FindAllReferencesAsync()
    {
        if (Documents.ActiveDocument is not { FilePath: not null } document
            || GetCaretOffset(document) is not { } offset)
        {
            return;
        }

        IReadOnlyList<SearchMatch> references;
        try
        {
            references = await document.GetReferencesAsync(offset);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (references.Count == 0)
        {
            StatusText = "No references found.";
            return;
        }

        var files = references.Select(match => match.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        ShowMatchesInSearchPanel($"{references.Count} reference(s) in {files} file(s).", references);
    }

    /// <summary>Renames the symbol at the caret across the workspace (F2); Roslyn for C#, LSP for TS/JS.</summary>
    [RelayCommand(CanExecute = nameof(CanUseSymbolServices))]
    private async Task RenameSymbolAsync()
    {
        if (Documents.ActiveDocument is not { FilePath: { } filePath } document
            || GetCaretOffset(document) is not { } offset)
        {
            return;
        }

        var text = document.Document.Text;
        var currentName = GetIdentifierAt(text, offset);
        var newName = _dialogService.ShowInputDialog(
            "Rename Symbol",
            $"Rename '{currentName ?? "symbol"}' to:",
            currentName);
        if (string.IsNullOrWhiteSpace(newName) || newName == currentName)
        {
            return;
        }

        IReadOnlyList<Core.Documents.FileTextChange>? changes;
        try
        {
            changes = await ResolveRenameChangesAsync(document, filePath, text, offset, newName);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (changes is null)
        {
            _dialogService.ShowError(
                "Rename Symbol",
                "The symbol could not be renamed (no renamable source symbol at the caret, or the name is not a valid identifier).");
            return;
        }

        foreach (var change in changes)
        {
            var openDocument = Documents.Documents.FirstOrDefault(candidate =>
                string.Equals(candidate.FilePath, change.FilePath, StringComparison.OrdinalIgnoreCase));
            if (openDocument is not null)
            {
                // One undoable edit; the tab goes dirty and the user saves as usual.
                openDocument.Document.Text = change.NewText;
            }
            else
            {
                try
                {
                    await _fileService.WriteTextAsync(change.FilePath, change.NewText);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _dialogService.ShowError("Rename Symbol", $"Could not update '{change.FilePath}'.\n\n{ex.Message}");
                }
            }
        }

        StatusText = $"Renamed to '{newName}' in {changes.Count} file(s).";
    }

    /// <summary>Formats the active document (Shift+Alt+F); Roslyn for C#, LSP for TS/JS.</summary>
    [RelayCommand(CanExecute = nameof(CanUseSymbolServices))]
    private async Task FormatDocumentAsync()
    {
        // IsReadOnly only blocks keyboard input in AvalonEdit; formatting edits the
        // buffer programmatically, so it must respect the flag itself.
        if (Documents.ActiveDocument is not { FilePath: not null, IsReadOnly: false } document)
        {
            return;
        }

        var snapshot = document.Document.Text;
        IReadOnlyList<Core.Documents.TextEditInfo>? edits;
        try
        {
            edits = await document.GetFormattingEditsAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (edits is null)
        {
            StatusText = "Formatting is unavailable for this file.";
            return;
        }

        // The edits target the snapshot; typing during the request invalidates them.
        if (!ReferenceEquals(Documents.ActiveDocument, document) || document.Document.Text != snapshot)
        {
            StatusText = "The document changed while formatting; no changes applied.";
            return;
        }

        if (edits.Count == 0)
        {
            StatusText = "The document is already formatted.";
            return;
        }

        using (document.Document.RunUpdate())
        {
            foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            {
                document.Document.Replace(edit.Start, edit.Length, edit.NewText);
            }
        }

        StatusText = $"Formatted document ({edits.Count} change(s)).";
    }

    // Definition, references, rename, and format all work for C# (Roslyn) and the
    // LSP languages; the command is enabled for a file-backed document of either.
    private bool CanUseSymbolServices()
        => Documents.ActiveDocument is { FilePath: not null } document
           && (document.Language.Id == "csharp" || LspLanguages.Includes(document.Language.Id));

    /// <summary>
    /// Computes the whole-file changes for a rename: Roslyn returns them directly;
    /// for LSP the per-file range edits are applied to each file's current content
    /// (the open buffer if the file is open, else disk) to reconstruct the new text.
    /// </summary>
    private async Task<IReadOnlyList<Core.Documents.FileTextChange>?> ResolveRenameChangesAsync(
        DocumentViewModel document, string filePath, string text, int offset, string newName)
    {
        if (document.Language.Id == "csharp")
        {
            return await _codeAnalysisService.RenameSymbolAsync(filePath, text, offset, newName);
        }

        if (!LspLanguages.Includes(document.Language.Id))
        {
            return null;
        }

        // Flush every open LSP document so the server's view matches the buffers the
        // edits will be applied to, then request the rename.
        foreach (var open in Documents.Documents.Where(candidate =>
            candidate.FilePath is not null && LspLanguages.Includes(candidate.Language.Id)))
        {
            await _lspService.NotifyDocumentChangedAsync(open.FilePath!, open.Document.Text);
        }

        var location = document.Document.GetLocation(Math.Clamp(offset, 0, document.Document.TextLength));
        var fileEdits = await _lspService.RenameSymbolAsync(filePath, location.Line - 1, location.Column - 1, newName);
        if (fileEdits is null)
        {
            return null;
        }

        var changes = new List<Core.Documents.FileTextChange>();
        foreach (var file in fileEdits)
        {
            if (await ResolveFileContentAsync(file.FilePath) is { } content)
            {
                changes.Add(new Core.Documents.FileTextChange(file.FilePath, Core.Documents.TextEditApplier.Apply(content, file.Edits)));
            }
        }

        return changes.Count > 0 ? changes : null;
    }

    /// <summary>Current content of a file: the open buffer if it is open, else the file on disk (null on read failure).</summary>
    private async Task<string?> ResolveFileContentAsync(string path)
    {
        var open = Documents.Documents.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (open is not null)
        {
            return open.Document.Text;
        }

        try
        {
            return await _fileService.ReadTextAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task NavigateToMatchAsync(SearchMatch match)
    {
        await Documents.OpenAsync(match.FilePath);

        var document = Documents.ActiveDocument;
        if (document?.FilePath is not null
            && string.Equals(document.FilePath, match.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            document.NavigateTo(match.LineNumber, match.Column, match.MatchLength);
        }
    }

    private void ShowMatchesInSearchPanel(string statusMessage, IReadOnlyList<SearchMatch> matches)
    {
        Search.ShowResults(statusMessage, matches);
        IsSidebarVisible = true;
        SearchResultsRequested?.Invoke(this, EventArgs.Empty);
    }

    private static int? GetCaretOffset(DocumentViewModel document)
    {
        try
        {
            return document.Document.GetOffset(document.CaretLine, document.CaretColumn);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? GetIdentifierAt(string text, int offset)
    {
        if (offset < 0 || offset > text.Length)
        {
            return null;
        }

        var start = offset;
        while (start > 0 && IsIdentifierChar(text[start - 1]))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && IsIdentifierChar(text[end]))
        {
            end++;
        }

        return end > start ? text[start..end] : null;

        static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    /// Snapshots the session (workspace, expanded folders, open tabs) into the
    /// settings model. Called from the window's closing handler — before the
    /// close-all flow starts removing tabs — and persisted by the exit save.
    /// </summary>
    public void CaptureSessionState()
    {
        var session = _settingsService.Settings.Session;
        session.WorkspacePath = Explorer.RootPath;
        session.ExpandedFolders = Explorer.GetExpandedFolders();
        session.OpenFiles = [.. Documents.Documents
            .Where(document => document.FilePath is not null)
            .Select(document => document.FilePath!)];
        session.ActiveFile = Documents.ActiveDocument?.FilePath;
    }

    /// <summary>
    /// Best-effort restore of the previous session after startup: reopen the
    /// workspace and its expansion state, then the tabs (skipping missing files),
    /// ending on the previously active one.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        if (!_settingsService.Settings.RestoreSession)
        {
            return;
        }

        var session = _settingsService.Settings.Session;

        if (session.WorkspacePath is { } workspacePath && Explorer.TryOpenWorkspace(workspacePath))
        {
            Explorer.RestoreExpandedFolders(session.ExpandedFolders);
        }

        foreach (var file in session.OpenFiles.Where(_fileService.FileExists))
        {
            await Documents.OpenAsync(file, addToRecent: false);
        }

        if (session.ActiveFile is { } activeFile && _fileService.FileExists(activeFile))
        {
            // Re-opening an already open file just activates its tab.
            await Documents.OpenAsync(activeFile, addToRecent: false);
        }
    }

    [RelayCommand]
    private void About() => _dialogService.ShowInformation(
        "About Code Editor",
        $"{ApplicationName}\n\nA modern, lightweight code editor for Windows.\nBuilt with WPF, AvalonEdit, and .NET.");

    private async Task OpenRecentAsync(string path)
    {
        if (!_fileService.FileExists(path))
        {
            _dialogService.ShowError("Open Recent", $"'{path}' no longer exists and will be removed from the list.");
            await _recentFilesService.RemoveAsync(path);
            return;
        }

        await Documents.OpenAsync(path);
    }

    private void RebuildRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _recentFilesService.RecentFiles)
        {
            RecentFiles.Add(new RecentFileViewModel(path, OpenRecentAsync));
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private void OnCodeAnalysisStatusChanged(object? sender, string status)
    {
        // Load progress is reported from background threads.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            StatusText = status;
        }
        else
        {
            dispatcher.BeginInvoke(() => StatusText = status);
        }
    }

    private void OnThemeChanged(object? sender, ThemeInfo theme)
    {
        _settingsService.Settings.Theme = theme.Id;
        foreach (var option in ThemeOptions)
        {
            option.IsSelected = option.Id == theme.Id;
        }
    }

    private void OnDocumentsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentsViewModel.ActiveDocument))
        {
            return;
        }

        if (_observedDocument is not null)
        {
            _observedDocument.PropertyChanged -= OnActiveDocumentPropertyChanged;
        }

        _observedDocument = Documents.ActiveDocument;
        if (_observedDocument is not null)
        {
            _observedDocument.PropertyChanged += OnActiveDocumentPropertyChanged;
        }

        GoToDefinitionCommand.NotifyCanExecuteChanged();
        FindAllReferencesCommand.NotifyCanExecuteChanged();
        RenameSymbolCommand.NotifyCanExecuteChanged();
        FormatDocumentCommand.NotifyCanExecuteChanged();
        UpdateTitle();
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.IsDirty)
            or nameof(DocumentViewModel.FileName)
            or nameof(DocumentViewModel.FilePath))
        {
            UpdateTitle();
        }
    }

    private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.RootName))
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        var segments = new List<string>(3);
        if (Documents.ActiveDocument is { } document)
        {
            segments.Add($"{document.FileName}{(document.IsDirty ? " ●" : string.Empty)}");
        }

        if (Explorer.RootName is { } rootName)
        {
            segments.Add(rootName);
        }

        segments.Add(ApplicationName);
        Title = string.Join(" — ", segments);
    }
}
