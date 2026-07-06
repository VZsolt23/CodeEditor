using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodeEditor.ViewModels;

/// <summary>
/// Manages the set of open documents (editor tabs): creating, opening,
/// saving, closing, and auto-saving.
/// </summary>
public sealed partial class DocumentsViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly IRecentFilesService _recentFilesService;
    private readonly ILanguageRegistry _languageRegistry;
    private readonly ISettingsService _settingsService;
    private readonly ICodeAnalysisService _codeAnalysis;
    private readonly EditorOptionsViewModel _options;
    private readonly ILogger<DocumentsViewModel> _logger;
    private readonly DispatcherTimer _autoSaveTimer;

    private int _untitledCounter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(SaveActiveCommand),
        nameof(SaveActiveAsCommand),
        nameof(SaveAllCommand),
        nameof(CloseActiveCommand))]
    private DocumentViewModel? _activeDocument;

    public DocumentsViewModel(
        IFileService fileService,
        IDialogService dialogService,
        IRecentFilesService recentFilesService,
        ILanguageRegistry languageRegistry,
        ISettingsService settingsService,
        ICodeAnalysisService codeAnalysis,
        EditorOptionsViewModel options,
        ILogger<DocumentsViewModel> logger)
    {
        _fileService = fileService;
        _dialogService = dialogService;
        _recentFilesService = recentFilesService;
        _languageRegistry = languageRegistry;
        _settingsService = settingsService;
        _codeAnalysis = codeAnalysis;
        _options = options;
        _logger = logger;

        var interval = Math.Max(1, settingsService.Settings.AutoSaveIntervalSeconds);
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        _autoSaveTimer.Tick += async (_, _) => await AutoSaveAsync();
        _autoSaveTimer.Start();
    }

    /// <summary>Open documents, in tab order.</summary>
    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    /// <summary>Whether any open document has unsaved changes.</summary>
    public bool HasDirtyDocuments => Documents.Any(document => document.IsDirty);

    [RelayCommand]
    private void NewDocument()
    {
        var name = $"Untitled-{++_untitledCounter}";
        var document = new DocumentViewModel(
            filePath: null,
            initialText: string.Empty,
            untitledName: name,
            language: LanguageInfo.PlainText,
            options: _options,
            codeAnalysis: _codeAnalysis,
            closeRequested: CloseDocumentAsync);

        Documents.Add(document);
        ActiveDocument = document;
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = _dialogService.ShowOpenFileDialog();
        if (path is not null)
        {
            await OpenAsync(path);
        }
    }

    /// <summary>
    /// Opens a file, activating the existing tab if it is already open.
    /// <paramref name="addToRecent"/> is false for session restore, which must
    /// not reshuffle the recent-files order.
    /// </summary>
    public async Task OpenAsync(string path, bool addToRecent = true)
    {
        var fullPath = System.IO.Path.GetFullPath(path);

        var existing = Documents.FirstOrDefault(document =>
            string.Equals(document.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveDocument = existing;
            return;
        }

        string text;
        try
        {
            text = await _fileService.ReadTextAsync(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogError(ex, "Failed to open {Path}", fullPath);
            _dialogService.ShowError("Open File", $"Could not open '{fullPath}'.\n\n{ex.Message}");
            return;
        }

        var document = new DocumentViewModel(
            filePath: fullPath,
            initialText: text,
            untitledName: System.IO.Path.GetFileName(fullPath),
            language: _languageRegistry.GetForFile(fullPath),
            options: _options,
            codeAnalysis: _codeAnalysis,
            closeRequested: CloseDocumentAsync);

        Documents.Add(document);
        ActiveDocument = document;
        if (addToRecent)
        {
            await _recentFilesService.AddAsync(fullPath);
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task SaveActiveAsync()
    {
        if (ActiveDocument is not null)
        {
            await SaveAsync(ActiveDocument);
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task SaveActiveAsAsync()
    {
        if (ActiveDocument is not null)
        {
            await SaveAsAsync(ActiveDocument);
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task SaveAllAsync()
    {
        foreach (var document in Documents.Where(document => document.IsDirty).ToList())
        {
            if (!await SaveAsync(document))
            {
                return;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task CloseActiveAsync()
    {
        if (ActiveDocument is not null)
        {
            await TryCloseDocumentAsync(ActiveDocument);
        }
    }

    /// <summary>Saves a document, falling back to Save As for untitled documents. Returns false if cancelled or failed.</summary>
    public async Task<bool> SaveAsync(DocumentViewModel document)
    {
        if (document.FilePath is null)
        {
            return await SaveAsAsync(document);
        }

        if (!await WriteDocumentAsync(document, document.FilePath))
        {
            return false;
        }

        document.MarkSaved();
        return true;
    }

    /// <summary>Prompts for a target file and saves the document there. Returns false if cancelled or failed.</summary>
    public async Task<bool> SaveAsAsync(DocumentViewModel document)
    {
        var path = _dialogService.ShowSaveFileDialog(document.FileName);
        if (path is null)
        {
            return false;
        }

        if (!await WriteDocumentAsync(document, path))
        {
            return false;
        }

        document.SetFile(path, _languageRegistry.GetForFile(path));
        document.MarkSaved();
        await _recentFilesService.AddAsync(path);
        return true;
    }

    /// <summary>Closes a document, prompting to save unsaved changes. Returns false if the user cancelled.</summary>
    public async Task<bool> TryCloseDocumentAsync(DocumentViewModel document)
    {
        if (document.IsDirty)
        {
            switch (_dialogService.ConfirmSaveChanges(document.FileName))
            {
                case ConfirmationResult.Cancel:
                    return false;
                case ConfirmationResult.Save when !await SaveAsync(document):
                    return false;
            }
        }

        var index = Documents.IndexOf(document);
        Documents.Remove(document);

        if (ReferenceEquals(ActiveDocument, document))
        {
            ActiveDocument = Documents.Count > 0
                ? Documents[Math.Min(index, Documents.Count - 1)]
                : null;
        }

        return true;
    }

    /// <summary>
    /// Rebinds open documents after a file or directory was renamed on disk
    /// (e.g. from the explorer), so tabs keep pointing at the right files.
    /// </summary>
    public void HandlePathRenamed(string oldPath, string newPath)
    {
        var oldPrefix = oldPath + System.IO.Path.DirectorySeparatorChar;
        foreach (var document in Documents)
        {
            if (document.FilePath is null)
            {
                continue;
            }

            if (string.Equals(document.FilePath, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                document.SetFile(newPath, _languageRegistry.GetForFile(newPath));
            }
            else if (document.FilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var movedPath = string.Concat(newPath, document.FilePath.AsSpan(oldPath.Length));
                document.SetFile(movedPath, _languageRegistry.GetForFile(movedPath));
            }
        }
    }

    /// <summary>Closes all documents, prompting per dirty document. Returns false if the user cancelled.</summary>
    public async Task<bool> TryCloseAllAsync()
    {
        foreach (var document in Documents.ToList())
        {
            if (!await TryCloseDocumentAsync(document))
            {
                return false;
            }
        }

        return true;
    }

    private async Task CloseDocumentAsync(DocumentViewModel document) => await TryCloseDocumentAsync(document);

    private bool HasActiveDocument() => ActiveDocument is not null;

    private async Task<bool> WriteDocumentAsync(DocumentViewModel document, string path)
    {
        try
        {
            await _fileService.WriteTextAsync(path, document.Document.Text);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogError(ex, "Failed to save {Path}", path);
            _dialogService.ShowError("Save File", $"Could not save '{path}'.\n\n{ex.Message}");
            return false;
        }
    }

    private async Task AutoSaveAsync()
    {
        if (!_settingsService.Settings.AutoSave)
        {
            return;
        }

        foreach (var document in Documents.Where(d => d.IsDirty && d.FilePath is not null).ToList())
        {
            try
            {
                await _fileService.WriteTextAsync(document.FilePath!, document.Document.Text);
                document.MarkSaved();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Silent by design: auto-save must not interrupt typing with dialogs.
                _logger.LogWarning(ex, "Auto-save failed for {Path}", document.FilePath);
            }
        }
    }
}
