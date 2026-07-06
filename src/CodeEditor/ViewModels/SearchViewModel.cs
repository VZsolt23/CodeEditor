using System.Collections.ObjectModel;
using System.IO;
using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodeEditor.ViewModels;

/// <summary>
/// Drives the Find in Files panel: query and options, running/cancelling the
/// search, the grouped results tree, and navigation to individual matches.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int MaxResults = 1000;

    private readonly ISearchService _searchService;
    private readonly IWorkspaceService _workspaceService;
    private readonly DocumentsViewModel _documents;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly Dictionary<string, SearchFileResultViewModel> _fileIndex = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _searchCts;
    private int _generation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SearchViewModel(
        ISearchService searchService,
        IWorkspaceService workspaceService,
        DocumentsViewModel documents,
        ILogger<SearchViewModel> logger)
    {
        _searchService = searchService;
        _workspaceService = workspaceService;
        _documents = documents;
        _logger = logger;

        workspaceService.WorkspaceChanged += OnWorkspaceChanged;
    }

    /// <summary>Search results grouped by file, in discovery order.</summary>
    public ObservableCollection<SearchFileResultViewModel> Results { get; } = [];

    /// <summary>
    /// Runs the search, cancelling any search still in flight. Concurrent execution is
    /// allowed so a new query can preempt a long-running one; a generation counter keeps
    /// stale results and status updates from the preempted run out of the UI.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (_workspaceService.RootPath is not { } rootPath)
        {
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var generation = ++_generation;

        Results.Clear();
        _fileIndex.Clear();
        IsSearching = true;
        StatusMessage = "Searching…";

        // Created on the UI thread, so reports from the background scan arrive here.
        var progress = new Progress<SearchMatch>(match =>
        {
            if (generation == _generation)
            {
                AddMatch(match, rootPath);
            }
        });

        try
        {
            var query = new SearchQuery(QueryText, MatchCase, WholeWord, MaxResults);
            var summary = await _searchService.SearchAsync(query, rootPath, progress, cts.Token);

            if (generation == _generation)
            {
                StatusMessage = FormatSummary(summary);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search or the workspace closed; nothing to report.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Search failed in {Root}", rootPath);
            if (generation == _generation)
            {
                StatusMessage = $"Search failed: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsSearching = false;
            }

            // Drop the shared reference before disposing so a later Cancel()
            // (new search, workspace change) can never hit a disposed CTS.
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Replaces the results tree with externally produced matches (e.g. Find All
    /// References), cancelling any running text search.
    /// </summary>
    public void ShowResults(string statusMessage, IReadOnlyList<SearchMatch> matches)
    {
        _searchCts?.Cancel();
        _generation++;

        Results.Clear();
        _fileIndex.Clear();
        IsSearching = false;

        var rootPath = _workspaceService.RootPath ?? string.Empty;
        foreach (var match in matches)
        {
            AddMatch(match, rootPath);
        }

        StatusMessage = statusMessage;
    }

    /// <summary>Opens the matched file and moves the caret to the match.</summary>
    public async Task OpenMatchAsync(SearchMatchViewModel match)
    {
        await _documents.OpenAsync(match.FilePath);

        var document = _documents.ActiveDocument;
        if (document?.FilePath is not null
            && string.Equals(document.FilePath, match.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            document.NavigateTo(match.LineNumber, match.Column, match.MatchLength);
        }
    }

    private bool CanSearch() => _workspaceService.HasWorkspace && !string.IsNullOrWhiteSpace(QueryText);

    private void AddMatch(SearchMatch match, string rootPath)
    {
        if (!_fileIndex.TryGetValue(match.FilePath, out var file))
        {
            file = new SearchFileResultViewModel(match.FilePath, rootPath);
            _fileIndex[match.FilePath] = file;
            Results.Add(file);
        }

        file.Matches.Add(new SearchMatchViewModel(match, this));
    }

    private static string FormatSummary(SearchSummary summary)
    {
        if (summary.MatchCount == 0)
        {
            return "No results found.";
        }

        var results = summary.MatchCount == 1 ? "result" : "results";
        var files = summary.FileCount == 1 ? "file" : "files";
        var text = $"{summary.MatchCount} {results} in {summary.FileCount} {files}.";
        return summary.WasTruncated ? $"{text} Showing the first {MaxResults} results." : text;
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        _searchCts?.Cancel();
        _generation++;

        Results.Clear();
        _fileIndex.Clear();
        StatusMessage = string.Empty;
        IsSearching = false;
        SearchCommand.NotifyCanExecuteChanged();
    }
}
