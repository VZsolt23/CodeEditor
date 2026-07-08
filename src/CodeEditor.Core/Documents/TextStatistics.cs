namespace CodeEditor.Core.Documents;

/// <summary>
/// Text-shape limits for what the editor control can handle. AvalonEdit formats a
/// whole logical line as one visual line, so extremely long lines (minified
/// bundles) freeze the UI regardless of language features — measured safe at
/// 100k chars per line and hung well before 500k. Total size is bounded too:
/// a document costs roughly 10× its file size in working set.
/// </summary>
public static class TextStatistics
{
    /// <summary>Longest single line the editor renders without stalling (measured).</summary>
    public const int SafeMaxLineLength = 100_000;

    /// <summary>Largest document the editor accepts (~100 MB of text; ~40 MB measured comfortable).</summary>
    public const int SafeMaxTextLength = 100_000_000;

    /// <summary>Length in characters of the longest line in <paramref name="text"/>.</summary>
    public static int MaxLineLength(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var max = 0;
        var lineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                var length = i - lineStart;
                if (length > 0 && text[i - 1] == '\r')
                {
                    length--;
                }

                max = Math.Max(max, length);
                lineStart = i + 1;
            }
        }

        return Math.Max(max, text.Length - lineStart);
    }

    /// <summary>
    /// Returns a human-readable reason the text is unsafe to load into the editor,
    /// or null when it is fine.
    /// </summary>
    public static string? GetEditorSafetyIssue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > SafeMaxTextLength)
        {
            return $"the file is too large for the editor ({text.Length / 1_000_000} MB of text)";
        }

        var maxLine = MaxLineLength(text);
        if (maxLine > SafeMaxLineLength)
        {
            return $"its longest line has {maxLine:N0} characters (the editor handles up to {SafeMaxLineLength:N0})";
        }

        return null;
    }
}
