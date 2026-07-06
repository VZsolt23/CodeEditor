namespace CodeEditor.Application.Interfaces;

/// <summary>
/// Tracks the most-recently-used files, newest first, trimmed to the configured limit.
/// </summary>
public interface IRecentFilesService
{
    /// <summary>Recent file paths, newest first.</summary>
    IReadOnlyList<string> RecentFiles { get; }

    /// <summary>Adds (or moves) a file to the top of the list and persists the change.</summary>
    Task AddAsync(string filePath);

    /// <summary>Removes a file from the list (e.g. when it no longer exists) and persists the change.</summary>
    Task RemoveAsync(string filePath);

    /// <summary>Raised whenever the list changes.</summary>
    event EventHandler? RecentFilesChanged;
}
