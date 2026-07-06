using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodeEditor.ViewModels;

/// <summary>
/// Drives the workspace explorer in the sidebar: opening/closing a folder,
/// exposing the lazily loaded folder tree, reacting to file system changes,
/// and performing explorer file operations with user prompts.
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly IWorkspaceService _workspaceService;
    private readonly DocumentsViewModel _documents;
    private readonly IDialogService _dialogService;
    private readonly ILogger<ExplorerViewModel> _logger;

    public ExplorerViewModel(
        IWorkspaceService workspaceService,
        DocumentsViewModel documents,
        IDialogService dialogService,
        ILogger<ExplorerViewModel> logger)
    {
        _workspaceService = workspaceService;
        _documents = documents;
        _dialogService = dialogService;
        _logger = logger;

        workspaceService.WorkspaceChanged += OnWorkspaceChanged;
        workspaceService.DirectoryContentsChanged += OnDirectoryContentsChanged;
    }

    /// <summary>The workspace root node (a single item), or empty when no folder is open.</summary>
    public ObservableCollection<FileTreeItemViewModel> Roots { get; } = [];

    public bool HasWorkspace => _workspaceService.HasWorkspace;

    /// <summary>Absolute path of the workspace root, or null when no folder is open.</summary>
    public string? RootPath => _workspaceService.RootPath;

    /// <summary>Display name of the workspace root folder, or null when no folder is open.</summary>
    public string? RootName => _workspaceService.RootPath is { } root
        ? (Path.GetFileName(root) is { Length: > 0 } name ? name : root)
        : null;

    [RelayCommand]
    private void OpenFolder()
    {
        var path = _dialogService.ShowOpenFolderDialog();
        if (path is null)
        {
            return;
        }

        try
        {
            _workspaceService.OpenWorkspace(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to open workspace {Path}", path);
            _dialogService.ShowError("Open Folder", $"Could not open '{path}'.\n\n{ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private void CloseFolder() => _workspaceService.CloseWorkspace();

    /// <summary>
    /// Opens a workspace without any dialogs (session restore); failures are
    /// logged and reported via the return value only.
    /// </summary>
    public bool TryOpenWorkspace(string rootPath)
    {
        try
        {
            _workspaceService.OpenWorkspace(rootPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not reopen workspace {Path}", rootPath);
            return false;
        }
    }

    /// <summary>Returns the paths of all expanded (and loaded) folders, parents before children.</summary>
    public List<string> GetExpandedFolders()
    {
        var expanded = new List<string>();
        foreach (var root in Roots)
        {
            Collect(root, expanded);
        }

        return expanded;

        static void Collect(FileTreeItemViewModel node, List<string> expanded)
        {
            if (!node.IsDirectory || !node.IsExpanded || !node.HasLoadedChildren)
            {
                return;
            }

            expanded.Add(node.FullPath);
            foreach (var child in node.Children)
            {
                Collect(child, expanded);
            }
        }
    }

    /// <summary>Re-expands previously expanded folders; missing paths are ignored.</summary>
    public void RestoreExpandedFolders(IEnumerable<string> folderPaths)
    {
        // Shortest first so parents load their children before descendants are looked up.
        foreach (var path in folderPaths.OrderBy(path => path.Length))
        {
            if (FindLoadedNode(path) is { IsDirectory: true } node)
            {
                node.IsExpanded = true;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private void Refresh() => Roots.FirstOrDefault()?.ReloadChildren();

    /// <summary>Opens a workspace file in an editor tab.</summary>
    public Task OpenFileAsync(string path) => _documents.OpenAsync(path);

    /// <summary>Prompts for a name and creates a file or folder inside <paramref name="directory"/>.</summary>
    public async Task CreateEntryAsync(FileTreeItemViewModel directory, bool createFolder)
    {
        var kind = createFolder ? "Folder" : "File";
        var name = _dialogService.ShowInputDialog($"New {kind}", $"Enter the name of the new {kind.ToLowerInvariant()}:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var path = createFolder
                ? _workspaceService.CreateDirectory(directory.FullPath, name)
                : _workspaceService.CreateFile(directory.FullPath, name);

            directory.IsExpanded = true;
            directory.ReloadChildren();

            if (!createFolder)
            {
                await _documents.OpenAsync(path);
            }
        }
        catch (Exception ex) when (IsFileOperationError(ex))
        {
            _logger.LogError(ex, "Failed to create {Kind} '{Name}' in {Path}", kind, name, directory.FullPath);
            _dialogService.ShowError($"New {kind}", ex.Message);
        }
    }

    /// <summary>Prompts for a new name and renames the entry, rebinding any open documents.</summary>
    public Task RenameAsync(FileTreeItemViewModel item)
    {
        var newName = _dialogService.ShowInputDialog("Rename", $"Rename '{item.Name}' to:", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, item.Name, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        try
        {
            var newPath = _workspaceService.Rename(item.FullPath, newName);
            _documents.HandlePathRenamed(item.FullPath, newPath);
            ReloadContainerOf(item);
        }
        catch (Exception ex) when (IsFileOperationError(ex))
        {
            _logger.LogError(ex, "Failed to rename {Path} to '{Name}'", item.FullPath, newName);
            _dialogService.ShowError("Rename", ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>Deletes the entry after confirmation.</summary>
    public Task DeleteAsync(FileTreeItemViewModel item)
    {
        var detail = item.IsDirectory ? " and all of its contents" : string.Empty;
        if (!_dialogService.Confirm("Delete", $"Are you sure you want to permanently delete '{item.Name}'{detail}?"))
        {
            return Task.CompletedTask;
        }

        try
        {
            _workspaceService.Delete(item.FullPath);
            ReloadContainerOf(item);
        }
        catch (Exception ex) when (IsFileOperationError(ex))
        {
            _logger.LogError(ex, "Failed to delete {Path}", item.FullPath);
            _dialogService.ShowError("Delete", ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>Copies a path to the clipboard, tolerating a locked clipboard.</summary>
    public void CopyPathToClipboard(string path)
    {
        try
        {
            Clipboard.SetText(path);
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "Failed to copy path to clipboard");
        }
    }

    /// <summary>Lists directory entries, returning an empty list (and logging) on I/O failure.</summary>
    public IReadOnlyList<FileSystemEntry> GetEntriesSafe(string directoryPath)
    {
        try
        {
            return _workspaceService.GetEntries(directoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to enumerate {Path}", directoryPath);
            return [];
        }
    }

    private static bool IsFileOperationError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void ReloadContainerOf(FileTreeItemViewModel item) => (item.Parent ?? item).ReloadChildren();

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        Roots.Clear();

        if (_workspaceService.RootPath is { } root)
        {
            var rootItem = new FileTreeItemViewModel(
                new FileSystemEntry(RootName!, root, IsDirectory: true),
                this,
                parent: null);
            Roots.Add(rootItem);
            rootItem.IsExpanded = true;
        }

        OnPropertyChanged(nameof(HasWorkspace));
        OnPropertyChanged(nameof(RootName));
        CloseFolderCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void OnDirectoryContentsChanged(object? sender, string directoryPath)
    {
        // Watcher events arrive on a background thread; tree updates must run on the UI thread.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => FindLoadedNode(directoryPath)?.ReloadChildren());
    }

    private FileTreeItemViewModel? FindLoadedNode(string path)
    {
        return Roots.Select(root => Find(root, path)).FirstOrDefault(match => match is not null);

        static FileTreeItemViewModel? Find(FileTreeItemViewModel node, string path)
        {
            if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (!node.HasLoadedChildren
                || !path.StartsWith(node.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return node.Children
                .Where(child => child.IsDirectory)
                .Select(child => Find(child, path))
                .FirstOrDefault(match => match is not null);
        }
    }
}
