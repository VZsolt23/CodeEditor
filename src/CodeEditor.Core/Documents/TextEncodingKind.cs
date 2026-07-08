namespace CodeEditor.Core.Documents;

/// <summary>
/// Text encodings the editor can detect and save. Deliberately a closed set
/// (the encodings that matter on Windows) rather than arbitrary code pages.
/// </summary>
public enum TextEncodingKind
{
    /// <summary>UTF-8 without a byte order mark (the default for new files).</summary>
    Utf8,

    /// <summary>UTF-8 with a byte order mark.</summary>
    Utf8Bom,

    /// <summary>UTF-16 little-endian (with BOM when saved).</summary>
    Utf16Le,

    /// <summary>UTF-16 big-endian (with BOM when saved).</summary>
    Utf16Be,

    /// <summary>UTF-32 little-endian (with BOM when saved).</summary>
    Utf32Le,

    /// <summary>UTF-32 big-endian (with BOM when saved).</summary>
    Utf32Be,

    /// <summary>Windows-1252 ("ANSI") — the fallback for non-UTF-8 legacy files.</summary>
    Ansi,
}

/// <summary>Display helpers for <see cref="TextEncodingKind"/>.</summary>
public static class TextEncodingKindExtensions
{
    /// <summary>Short human-readable name, as shown in the status bar.</summary>
    public static string DisplayName(this TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => "UTF-8",
        TextEncodingKind.Utf8Bom => "UTF-8 with BOM",
        TextEncodingKind.Utf16Le => "UTF-16 LE",
        TextEncodingKind.Utf16Be => "UTF-16 BE",
        TextEncodingKind.Utf32Le => "UTF-32 LE",
        TextEncodingKind.Utf32Be => "UTF-32 BE",
        TextEncodingKind.Ansi => "Windows-1252",
        _ => kind.ToString(),
    };
}
