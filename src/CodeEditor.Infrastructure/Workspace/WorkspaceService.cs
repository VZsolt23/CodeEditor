using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Infrastructure.Workspace;

/// <summary>
/// File-system-backed <see cref="IWorkspaceService"/>. Watches the workspace root
/// with a <see cref="FileSystemWatcher"/> and coalesces bursts of change events into
/// per-directory <see cref="DirectoryContentsChanged"/> notifications.
/// </summary>
public sealed class WorkspaceService : IWorkspaceService, IDisposable
{
    private static readonly TimeSpan ChangeDebounceInterval = TimeSpan.FromMilliseconds(300);

    private readonly ISettingsService _settingsService;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly object _pendingLock = new();
    private readonly HashSet<string> _pendingDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _flushTimer;

    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public WorkspaceService(ISettingsService settingsService, ILogger<WorkspaceService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _flushTimer = new Timer(_ => FlushPendingChanges(), state: null, Timeout.Infinite, Timeout.Infinite);
    }

    public string? RootPath { get; private set; }

    public bool HasWorkspace => RootPath is not null;

    public event EventHandler? WorkspaceChanged;

    public event EventHandler<string>? DirectoryContentsChanged;

    public void OpenWorkspace(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory '{fullPath}' does not exist.");
        }

        StopWatching();
        RootPath = fullPath;
        StartWatching(fullPath);

        _logger.LogInformation("Opened workspace {Path}", fullPath);
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseWorkspace()
    {
        if (RootPath is null)
        {
            return;
        }

        StopWatching();
        _logger.LogInformation("Closed workspace {Path}", RootPath);
        RootPath = null;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<FileSystemEntry> GetEntries(string directoryPath)
    {
        var directories = Directory.EnumerateDirectories(directoryPath)
            .Where(path => !IsExcluded(Path.GetFileName(path)))
            .Select(path => new FileSystemEntry(Path.GetFileName(path), path, IsDirectory: true));

        var files = Directory.EnumerateFiles(directoryPath)
            .Select(path => new FileSystemEntry(Path.GetFileName(path), path, IsDirectory: false));

        return
        [
            .. directories.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            .. files.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public string CreateFile(string directoryPath, string name)
    {
        var path = Path.Combine(directoryPath, ValidateName(name));
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
        }

        return path;
    }

    public string CreateDirectory(string directoryPath, string name)
    {
        var path = Path.Combine(directoryPath, ValidateName(name));
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException($"'{path}' already exists.");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public string Rename(string fullPath, string newName)
    {
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"'{fullPath}' has no parent directory.", nameof(fullPath));
        var newPath = Path.Combine(parent, ValidateName(newName));

        if (string.Equals(newPath, fullPath, StringComparison.Ordinal))
        {
            return fullPath;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Move(fullPath, newPath);
        }
        else
        {
            File.Move(fullPath, newPath);
        }

        return newPath;
    }

    public void Delete(string fullPath)
    {
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
        else
        {
            File.Delete(fullPath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWatching();
        _flushTimer.Dispose();
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"'{name}' is not a valid file or folder name.", nameof(name));
        }

        return name.Trim();
    }

    private void StartWatching(string rootPath)
    {
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;

        lock (_pendingLock)
        {
            _pendingDirectories.Clear();
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        => QueueChangedDirectory(Path.GetDirectoryName(e.FullPath));

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        QueueChangedDirectory(Path.GetDirectoryName(e.OldFullPath));
        QueueChangedDirectory(Path.GetDirectoryName(e.FullPath));
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // The watcher buffer overflowed; individual events were lost, so refresh from the root.
        _logger.LogWarning(e.GetException(), "Workspace watcher error; requesting a full refresh");
        QueueChangedDirectory(RootPath);
    }

    private void QueueChangedDirectory(string? directoryPath)
    {
        if (directoryPath is null || IsInsideExcludedFolder(directoryPath))
        {
            return;
        }

        lock (_pendingLock)
        {
            _pendingDirectories.Add(directoryPath);
        }

        _flushTimer.Change(ChangeDebounceInterval, Timeout.InfiniteTimeSpan);
    }

    private void FlushPendingChanges()
    {
        string[] directories;
        lock (_pendingLock)
        {
            directories = [.. _pendingDirectories];
            _pendingDirectories.Clear();
        }

        foreach (var directory in directories)
        {
            DirectoryContentsChanged?.Invoke(this, directory);
        }
    }

    private bool IsInsideExcludedFolder(string directoryPath)
    {
        var root = RootPath;
        if (root is null)
        {
            return true;
        }

        var relative = Path.GetRelativePath(root, directoryPath);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return relative != ".";
        }

        return relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(IsExcluded);
    }

    private bool IsExcluded(string? folderName)
        => folderName is not null
           && _settingsService.Settings.ExplorerExcludedFolders
               .Contains(folderName, StringComparer.OrdinalIgnoreCase);
}
