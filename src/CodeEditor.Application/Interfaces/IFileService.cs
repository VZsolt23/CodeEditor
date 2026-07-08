using CodeEditor.Core.Documents;

namespace CodeEditor.Application.Interfaces;

/// <summary>The text and detected encoding of a file read by <see cref="IFileService"/>.</summary>
/// <param name="Text">The decoded file content.</param>
/// <param name="Encoding">The encoding the file was decoded with (see <see cref="TextEncodingDetector"/>).</param>
public sealed record TextFileReadResult(string Text, TextEncodingKind Encoding);

/// <summary>
/// Abstraction over file system access used by document management,
/// keeping ViewModels testable and free of direct I/O.
/// </summary>
public interface IFileService
{
    /// <summary>Reads the entire file as text, detecting the encoding from the BOM (UTF-8 by default).</summary>
    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the entire file as text, detecting the encoding (BOMs, BOM-less UTF-16,
    /// UTF-8 validation, Windows-1252 fallback) and reporting what was detected so
    /// the file can be saved back in the same encoding.
    /// </summary>
    Task<TextFileReadResult> ReadTextWithEncodingAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Writes text to a file (UTF-8, no BOM), creating the containing directory if needed.</summary>
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>Writes text to a file in the given encoding, creating the containing directory if needed.</summary>
    Task WriteTextAsync(string path, string content, TextEncodingKind encoding, CancellationToken cancellationToken = default);

    /// <summary>Returns whether the file exists.</summary>
    bool FileExists(string path);
}
