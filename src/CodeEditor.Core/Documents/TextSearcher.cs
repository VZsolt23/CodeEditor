namespace CodeEditor.Core.Documents;

/// <summary>
/// Literal-text matching shared by the in-editor find/replace and Find in Files.
/// </summary>
public static class TextSearcher
{
    /// <summary>
    /// Finds all non-overlapping occurrences of <paramref name="query"/> in <paramref name="text"/>.
    /// </summary>
    public static List<TextSpan> FindAll(string text, string query, bool matchCase, bool wholeWord)
    {
        var matches = new List<TextSpan>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return matches;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = 0;
        while (index < text.Length && (index = text.IndexOf(query, index, comparison)) >= 0)
        {
            if (!wholeWord || IsWholeWordAt(text, index, query.Length))
            {
                matches.Add(new TextSpan(index, query.Length));
            }

            index += query.Length;
        }

        return matches;
    }

    /// <summary>Whether the occurrence at <paramref name="index"/> is bounded by non-word characters.</summary>
    public static bool IsWholeWordAt(string text, int index, int length)
    {
        return (index == 0 || !IsWordChar(text[index - 1]))
               && (index + length >= text.Length || !IsWordChar(text[index + length]));

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
