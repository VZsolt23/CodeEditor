namespace CodeEditor.Core.Workspace;

/// <summary>
/// Outcome of a completed workspace search.
/// </summary>
/// <param name="MatchCount">Total number of matches reported.</param>
/// <param name="FileCount">Number of files containing at least one match.</param>
/// <param name="WasTruncated">Whether the search stopped early because <see cref="SearchQuery.MaxResults"/> was reached.</param>
public sealed record SearchSummary(int MatchCount, int FileCount, bool WasTruncated);
