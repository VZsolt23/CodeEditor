using CodeEditor.Core.Workspace;

namespace CodeEditor.Application.Interfaces;

/// <summary>
/// Manages the folder opened as the current workspace: enumerating its contents
/// (honoring the configured exclude list), watching it for external changes,
/// and performing explorer file operations.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>Absolute path of the workspace root, or null when no folder is open.</summary>
    string? RootPath { get; }

    /// <summary>Whether a folder is currently open as a workspace.</summary>
    bool HasWorkspace { get; }

    /// <summary>Raised after the workspace is opened, reopened, or closed.</summary>
    event EventHandler? WorkspaceChanged;

    /// <summary>
    /// Raised when the contents of a directory inside the workspace change on disk.
    /// The argument is the absolute path of the affected directory. May be raised
    /// on a background thread; subscribers must marshal to the UI thread themselves.
    /// </summary>
    event EventHandler<string>? DirectoryContentsChanged;

    /// <summary>Opens <paramref name="rootPath"/> as the workspace and starts watching it.</summary>
    /// <exception cref="System.IO.DirectoryNotFoundException">The directory does not exist.</exception>
    void OpenWorkspace(string rootPath);

    /// <summary>Closes the current workspace and stops watching, if one is open.</summary>
    void CloseWorkspace();

    /// <summary>
    /// Lists the entries of a directory, excluding configured folder names,
    /// directories first, each group sorted by name.
    /// </summary>
    IReadOnlyList<FileSystemEntry> GetEntries(string directoryPath);

    /// <summary>Creates an empty file named <paramref name="name"/> in <paramref name="directoryPath"/> and returns its full path.</summary>
    string CreateFile(string directoryPath, string name);

    /// <summary>Creates a subdirectory named <paramref name="name"/> in <paramref name="directoryPath"/> and returns its full path.</summary>
    string CreateDirectory(string directoryPath, string name);

    /// <summary>Renames the file or directory at <paramref name="fullPath"/> to <paramref name="newName"/> and returns the new full path.</summary>
    string Rename(string fullPath, string newName);

    /// <summary>Deletes the file or directory (recursively) at <paramref name="fullPath"/>.</summary>
    void Delete(string fullPath);
}
