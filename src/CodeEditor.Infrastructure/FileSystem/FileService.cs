using System.Text;
using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Documents;

namespace CodeEditor.Infrastructure.FileSystem;

/// <summary>
/// Default <see cref="IFileService"/> implementation backed by the local file system.
/// Reads detect the file's encoding (<see cref="TextEncodingDetector"/>); writes can
/// target any <see cref="TextEncodingKind"/> so saving preserves the original encoding.
/// </summary>
public sealed class FileService : IFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly Encoding Ansi;

    static FileService()
    {
        // Windows-1252 lives in the CodePages provider on .NET (not built in).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Ansi = Encoding.GetEncoding(1252);
    }

    public async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await ReadTextWithEncodingAsync(path, cancellationToken).ConfigureAwait(false);
        return result.Text;
    }

    public async Task<TextFileReadResult> ReadTextWithEncodingAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var kind = TextEncodingDetector.Detect(bytes);
        var encoding = GetEncoding(kind);

        // GetString keeps a leading BOM as U+FEFF text; skip it so it can't be
        // edited into the middle of the file (it is re-added on save).
        var preamble = encoding.Preamble;
        var offset = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        return new TextFileReadResult(text, kind);
    }

    public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => WriteTextAsync(path, content, TextEncodingKind.Utf8, cancellationToken);

    public async Task WriteTextAsync(string path, string content, TextEncodingKind encoding, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, GetEncoding(encoding), cancellationToken).ConfigureAwait(false);
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool IsReadOnly(string path)
    {
        try
        {
            return File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Maps an encoding kind to the <see cref="Encoding"/> used to decode and save it.</summary>
    private static Encoding GetEncoding(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => Utf8NoBom,
        TextEncodingKind.Utf8Bom => Utf8Bom,
        TextEncodingKind.Utf16Le => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        TextEncodingKind.Utf16Be => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        TextEncodingKind.Utf32Le => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
        TextEncodingKind.Utf32Be => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
        TextEncodingKind.Ansi => Ansi,
        _ => Utf8NoBom,
    };
}
