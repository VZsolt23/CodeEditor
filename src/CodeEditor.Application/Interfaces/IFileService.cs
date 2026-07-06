namespace CodeEditor.Application.Interfaces;

/// <summary>
/// Abstraction over file system access used by document management,
/// keeping ViewModels testable and free of direct I/O.
/// </summary>
public interface IFileService
{
    /// <summary>Reads the entire file as text, detecting the encoding from the BOM (UTF-8 by default).</summary>
    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Writes text to a file (UTF-8, no BOM), creating the containing directory if needed.</summary>
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>Returns whether the file exists.</summary>
    bool FileExists(string path);
}
