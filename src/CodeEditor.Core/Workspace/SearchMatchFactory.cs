namespace CodeEditor.Core.Workspace;

/// <summary>
/// Builds <see cref="SearchMatch"/> instances with consistent display windowing
/// for very long lines. Shared by Find in Files and the Roslyn-backed
/// definition/reference results.
/// </summary>
public static class SearchMatchFactory
{
    private const int MaxLineDisplayLength = 250;
    private const int DisplayContextBeforeMatch = 60;

    /// <summary>
    /// Creates a match for <paramref name="lineText"/> (the full line), windowing
    /// the display text around the match when the line is very long.
    /// </summary>
    /// <param name="path">Absolute file path.</param>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="lineText">Full text of the line.</param>
    /// <param name="matchStartInLine">0-based match start within the line.</param>
    /// <param name="matchLength">Match length in characters.</param>
    public static SearchMatch Create(string path, int lineNumber, string lineText, int matchStartInLine, int matchLength)
    {
        var displayText = lineText;
        var displayStart = matchStartInLine;

        if (lineText.Length > MaxLineDisplayLength)
        {
            var windowStart = Math.Max(0, matchStartInLine - DisplayContextBeforeMatch);
            displayText = lineText.Substring(windowStart, Math.Min(MaxLineDisplayLength, lineText.Length - windowStart));
            displayStart = matchStartInLine - windowStart;
        }

        var displayLength = Math.Clamp(matchLength, 0, Math.Max(0, displayText.Length - displayStart));
        return new SearchMatch(path, lineNumber, matchStartInLine + 1, matchLength, displayText, displayStart, displayLength);
    }
}
