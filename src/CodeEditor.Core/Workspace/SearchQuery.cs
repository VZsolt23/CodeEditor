namespace CodeEditor.Core.Workspace;

/// <summary>
/// Parameters of a workspace-wide text search.
/// </summary>
/// <param name="Text">The literal text to search for.</param>
/// <param name="MatchCase">Whether the comparison is case sensitive.</param>
/// <param name="WholeWord">Whether matches must be bounded by non-word characters.</param>
/// <param name="MaxResults">Upper bound on reported matches; the search stops once reached.</param>
public sealed record SearchQuery(string Text, bool MatchCase, bool WholeWord, int MaxResults = 1000);
