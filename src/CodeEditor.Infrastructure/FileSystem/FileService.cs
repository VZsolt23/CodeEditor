using System.Text;
using CodeEditor.Application.Interfaces;

namespace CodeEditor.Infrastructure.FileSystem;

/// <summary>
/// Default <see cref="IFileService"/> implementation backed by the local file system.
/// </summary>
public sealed class FileService : IFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // StreamReader detects UTF-8/UTF-16/UTF-32 BOMs and falls back to UTF-8.
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    public bool FileExists(string path) => File.Exists(path);
}
