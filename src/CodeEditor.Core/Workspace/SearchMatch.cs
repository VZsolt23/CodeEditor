namespace CodeEditor.Core.Workspace;

/// <summary>
/// A single match found by a workspace search.
/// </summary>
/// <param name="FilePath">Absolute path of the file containing the match.</param>
/// <param name="LineNumber">1-based line number of the match.</param>
/// <param name="Column">1-based column of the match within the full line (for caret navigation).</param>
/// <param name="MatchLength">Length of the matched text (for selection).</param>
/// <param name="LineText">Display excerpt of the line; long lines are windowed around the match.</param>
/// <param name="LineTextMatchStart">0-based start of the match within <see cref="LineText"/>.</param>
/// <param name="LineTextMatchLength">Length of the match within <see cref="LineText"/> (may be clipped by the window).</param>
public sealed record SearchMatch(
    string FilePath,
    int LineNumber,
    int Column,
    int MatchLength,
    string LineText,
    int LineTextMatchStart,
    int LineTextMatchLength);
