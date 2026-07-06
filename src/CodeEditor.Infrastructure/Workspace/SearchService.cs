using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Documents;
using CodeEditor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace CodeEditor.Infrastructure.Workspace;

/// <summary>
/// File-system implementation of <see cref="ISearchService"/>: a plain-text scan
/// of all workspace files, skipping excluded folders, binary files (NUL sniff),
/// and oversized files. The scan runs on a thread-pool thread.
/// </summary>
public sealed class SearchService : ISearchService
{
    private const int BinarySniffLength = 1024;
    private const long MaxFileSizeBytes = 16 * 1024 * 1024;

    private readonly ISettingsService _settingsService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(ISettingsService settingsService, ILogger<SearchService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<SearchSummary> SearchAsync(
        SearchQuery query,
        string rootPath,
        IProgress<SearchMatch> results,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrEmpty(query.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(results);

        return await Task
            .Run(() => SearchCore(query, rootPath, results, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private SearchSummary SearchCore(
        SearchQuery query, string rootPath, IProgress<SearchMatch> results, CancellationToken cancellationToken)
    {
        var matchCount = 0;
        var fileCount = 0;
        var truncated = false;

        foreach (var file in EnumerateFiles(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = SearchFile(file, query, query.MaxResults - matchCount, results, cancellationToken);
            if (found > 0)
            {
                matchCount += found;
                fileCount++;
            }

            if (matchCount >= query.MaxResults)
            {
                truncated = true;
                break;
            }
        }

        return new SearchSummary(matchCount, fileCount, truncated);
    }

    /// <summary>Walks the workspace tree, skipping excluded folders and unreadable directories.</summary>
    private IEnumerable<string> EnumerateFiles(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            string[] subdirectories;
            string[] files;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Skipped unreadable directory {Path} during search", directory);
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                if (!IsExcluded(Path.GetFileName(subdirectory)))
                {
                    pending.Push(subdirectory);
                }
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private int SearchFile(
        string path, SearchQuery query, int remaining,
        IProgress<SearchMatch> results, CancellationToken cancellationToken)
    {
        var count = 0;
        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > MaxFileSizeBytes)
            {
                return 0;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (LooksBinary(stream))
            {
                return 0;
            }

            stream.Position = 0;
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

            var lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                if ((lineNumber & 0xFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                foreach (var span in TextSearcher.FindAll(line, query.Text, query.MatchCase, query.WholeWord))
                {
                    results.Report(SearchMatchFactory.Create(path, lineNumber, line, span.Start, span.Length));
                    count++;
                    if (count >= remaining)
                    {
                        return count;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Skipped unreadable file {Path} during search", path);
        }

        return count;
    }

    private static bool LooksBinary(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[BinarySniffLength];
        var read = stream.Read(buffer);
        return buffer[..read].Contains((byte)0);
    }

    private bool IsExcluded(string? folderName)
        => folderName is not null
           && _settingsService.Settings.ExplorerExcludedFolders
               .Contains(folderName, StringComparer.OrdinalIgnoreCase);
}
