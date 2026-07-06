using CodeEditor.Core.Workspace;

namespace CodeEditor.Application.Interfaces;

/// <summary>
/// Workspace-wide text search. Honors the explorer exclude list and skips
/// binary and oversized files.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches all files under <paramref name="rootPath"/> for <paramref name="query"/>,
    /// streaming matches to <paramref name="results"/> as they are found. The scan runs on
    /// a background thread; an <see cref="IProgress{T}"/> created on the UI thread receives
    /// matches there.
    /// </summary>
    Task<SearchSummary> SearchAsync(
        SearchQuery query,
        string rootPath,
        IProgress<SearchMatch> results,
        CancellationToken cancellationToken = default);
}
