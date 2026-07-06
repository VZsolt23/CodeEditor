using CodeEditor.Application.Interfaces;
using CodeEditor.Core.Completion;
using CodeEditor.Core.Workspace;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using CoreDiagnostics = CodeEditor.Core.Diagnostics;
using CoreDocuments = CodeEditor.Core.Documents;

namespace CodeEditor.Infrastructure.Roslyn;

/// <summary>
/// Roslyn-backed <see cref="ICodeAnalysisService"/>: hosts an
/// <see cref="MSBuildWorkspace"/> for the opened folder and computes per-document
/// semantic diagnostics against in-editor (unsaved) text. Everything is
/// best-effort — a missing SDK or a failed project load degrades to syntax-only
/// diagnostics instead of failing the app.
/// </summary>
public sealed class RoslynWorkspaceService : ICodeAnalysisService, IDisposable
{
    private const int MaxDiagnosticsPerFile = 500;
    private const int MaxProjectsWithoutSolution = 20;

    private static readonly object MsBuildRegistrationLock = new();
    private static bool _msBuildRegistrationAttempted;
    private static bool _msBuildAvailable;

    private readonly ISettingsService _settingsService;
    private readonly ILogger<RoslynWorkspaceService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private (Document Document, CompletionList List)? _lastCompletion;

    public RoslynWorkspaceService(ISettingsService settingsService, ILogger<RoslynWorkspaceService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsLoaded => _solution is not null;

    public event EventHandler<string>? StatusChanged;

    public async Task LoadAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        await Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                UnloadCore(status: null);

                if (!TryRegisterMsBuild(out var unavailableReason))
                {
                    Report($"C# language services unavailable: {unavailableReason}");
                    return;
                }

                var (solutionPath, projectPaths) = FindLoadTarget(rootPath);
                if (solutionPath is null && projectPaths.Count == 0)
                {
                    Report("No C# solution or projects found in this folder.");
                    return;
                }

                Report("Loading C# workspace…");
                await OpenAsync(solutionPath, projectPaths, cancellationToken).ConfigureAwait(false);

                var projectCount = _solution?.Projects.Count() ?? 0;
                Report(projectCount > 0
                    ? $"C# ready: {projectCount} project(s) loaded."
                    : "C# workspace loaded no projects; falling back to syntax-only diagnostics.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort loader: whatever MSBuild/Roslyn throws here must not
                // take the app down — the editor keeps working without semantics.
                _logger.LogError(ex, "Failed to load C# workspace from {Root}", rootPath);
                UnloadCore(status: null);
                Report($"Failed to load C# workspace: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Unload()
    {
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_workspace is not null)
                {
                    UnloadCore("C# workspace closed.");
                }
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public async Task<IReadOnlyList<CoreDiagnostics.DiagnosticItem>> GetDiagnosticsAsync(
        string filePath, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        return await Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return ParseLooseFile(filePath, text, cancellationToken);
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (semanticModel is null)
                {
                    return ParseLooseFile(filePath, text, cancellationToken);
                }

                return Map(semanticModel.GetDiagnostics(cancellationToken: cancellationToken), filePath);
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompletionResultInfo?> GetCompletionsAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        return await Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _lastCompletion = null;

                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return null;
                }

                var completionService = CompletionService.GetService(document);
                if (completionService is null)
                {
                    return null;
                }

                var clampedOffset = Math.Clamp(offset, 0, text.Length);
                var list = await completionService
                    .GetCompletionsAsync(document, clampedOffset, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (list is null || list.ItemsList.Count == 0)
                {
                    return null;
                }

                _lastCompletion = (document, list);

                var items = new List<CompletionItemInfo>(list.ItemsList.Count);
                for (var i = 0; i < list.ItemsList.Count; i++)
                {
                    var item = list.ItemsList[i];
                    items.Add(new CompletionItemInfo(
                        i,
                        item.DisplayText + item.DisplayTextSuffix,
                        item.FilterText,
                        item.SortText,
                        item.Tags is [var firstTag, ..] ? firstTag : string.Empty));
                }

                return new CompletionResultInfo(list.Span.Start, list.Span.Length, items);
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompletionChangeInfo?> GetCompletionChangeAsync(
        int itemIndex, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_lastCompletion is not { } completion
                    || itemIndex < 0
                    || itemIndex >= completion.List.ItemsList.Count)
                {
                    return null;
                }

                var completionService = CompletionService.GetService(completion.Document);
                if (completionService is null)
                {
                    return null;
                }

                var change = await completionService
                    .GetChangeAsync(completion.Document, completion.List.ItemsList[itemIndex], cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var textChange = change.TextChange;
                return new CompletionChangeInfo(textChange.Span.Start, textChange.Span.Length, textChange.NewText ?? string.Empty);
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetQuickInfoAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        return await Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return null;
                }

                var quickInfoService = QuickInfoService.GetService(document);
                if (quickInfoService is null)
                {
                    return null;
                }

                var item = await quickInfoService
                    .GetQuickInfoAsync(document, Math.Clamp(offset, 0, text.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (item is null)
                {
                    return null;
                }

                var sections = item.Sections
                    .Select(section => section.Text)
                    .Where(sectionText => !string.IsNullOrWhiteSpace(sectionText));
                var combined = string.Join(Environment.NewLine + Environment.NewLine, sections);
                return combined.Length == 0 ? null : combined;
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SignatureHelpInfo?> GetSignatureHelpAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        return await Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return null;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (root is null)
                {
                    return null;
                }

                var position = Math.Clamp(offset, 0, text.Length);
                var argumentList = root
                    .FindToken(Math.Max(0, position - 1))
                    .Parent?
                    .AncestorsAndSelf()
                    .OfType<BaseArgumentListSyntax>()
                    .FirstOrDefault(list => list.SpanStart < position && position <= list.Span.End);
                if (argumentList is null)
                {
                    return null;
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (semanticModel is null)
                {
                    return null;
                }

                var methods = GetCandidateMethods(semanticModel, argumentList, cancellationToken);
                if (methods.Count == 0)
                {
                    return null;
                }

                var activeParameter = argumentList.Arguments.GetSeparators()
                    .Count(separator => separator.SpanStart < position);
                var activeSignature = methods.FindIndex(method =>
                    method.Parameters.Length > activeParameter
                    || method.Parameters.LastOrDefault()?.IsParams == true);

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var signatures = new List<string>();
                foreach (var method in methods)
                {
                    var signature = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    if (seen.Add(signature))
                    {
                        signatures.Add(signature);
                    }
                }

                return new SignatureHelpInfo(signatures, Math.Max(0, activeSignature), activeParameter);
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves the overload candidates for the call owning <paramref name="argumentList"/>.</summary>
    private static List<IMethodSymbol> GetCandidateMethods(
        SemanticModel semanticModel, BaseArgumentListSyntax argumentList, CancellationToken cancellationToken)
    {
        switch (argumentList.Parent)
        {
            case InvocationExpressionSyntax invocation:
                var group = semanticModel
                    .GetMemberGroup(invocation.Expression, cancellationToken)
                    .OfType<IMethodSymbol>()
                    .ToList();
                if (group.Count > 0)
                {
                    return group;
                }

                var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
                return symbolInfo.Symbol is IMethodSymbol resolved
                    ? [resolved]
                    : [.. symbolInfo.CandidateSymbols.OfType<IMethodSymbol>()];

            case BaseObjectCreationExpressionSyntax creation:
                return semanticModel.GetTypeInfo(creation, cancellationToken).Type is INamedTypeSymbol type
                    ? [.. type.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility != Accessibility.Private)]
                    : [];

            default:
                return [];
        }
    }

    public async Task<IReadOnlyList<SearchMatch>> GetDefinitionsAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default)
    {
        return await RunSymbolQueryAsync(filePath, text, offset, async (symbol, _, ct) =>
        {
            var matches = new List<SearchMatch>();
            foreach (var location in symbol.Locations.Where(location => location.IsInSource))
            {
                if (await MapLocationAsync(location, ct).ConfigureAwait(false) is { } match)
                {
                    matches.Add(match);
                }
            }

            return SortMatches(matches);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchMatch>> FindReferencesAsync(
        string filePath, string text, int offset, CancellationToken cancellationToken = default)
    {
        return await RunSymbolQueryAsync(filePath, text, offset, async (symbol, solution, ct) =>
        {
            var locations = new List<Location>(symbol.Locations.Where(location => location.IsInSource));
            foreach (var referenced in await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false))
            {
                locations.AddRange(referenced.Locations.Select(reference => reference.Location));
            }

            var seen = new HashSet<(string?, int)>();
            var matches = new List<SearchMatch>();
            foreach (var location in locations)
            {
                if (!location.IsInSource || !seen.Add((location.SourceTree?.FilePath, location.SourceSpan.Start)))
                {
                    continue;
                }

                if (await MapLocationAsync(location, ct).ConfigureAwait(false) is { } match)
                {
                    matches.Add(match);
                }
            }

            return SortMatches(matches);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CoreDocuments.ClassifiedSpanInfo>> GetClassificationsAsync(
        string filePath, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        return await Task.Run<IReadOnlyList<CoreDocuments.ClassifiedSpanInfo>>(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return [];
                }

                var classified = await Classifier
                    .GetClassifiedSpansAsync(document, new TextSpan(0, text.Length), cancellationToken)
                    .ConfigureAwait(false);

                // Skip lexical noise the colorizer never colors; keep the rest sorted.
                return [.. classified
                    .Where(span => span.ClassificationType
                        is not (ClassificationTypeNames.Punctuation
                        or ClassificationTypeNames.Operator
                        or ClassificationTypeNames.WhiteSpace
                        or ClassificationTypeNames.Text
                        or ClassificationTypeNames.StaticSymbol))
                    .OrderBy(span => span.TextSpan.Start)
                    .Select(span => new CoreDocuments.ClassifiedSpanInfo(
                        span.TextSpan.Start, span.TextSpan.Length, span.ClassificationType))];
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CoreDocuments.TextEditInfo>?> FormatDocumentAsync(
        string filePath, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        return await Task.Run<IReadOnlyList<CoreDocuments.TextEditInfo>?>(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return null;
                }

                var formattedDocument = await Formatter
                    .FormatAsync(document, options: null, cancellationToken)
                    .ConfigureAwait(false);
                var changes = await formattedDocument
                    .GetTextChangesAsync(document, cancellationToken)
                    .ConfigureAwait(false);

                return [.. changes.Select(change =>
                    new CoreDocuments.TextEditInfo(change.Span.Start, change.Span.Length, change.NewText ?? string.Empty))];
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CoreDocuments.FileTextChange>?> RenameSymbolAsync(
        string filePath, string text, int offset, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        if (!SyntaxFacts.IsValidIdentifier(newName))
        {
            return null;
        }

        return await Task.Run<IReadOnlyList<CoreDocuments.FileTextChange>?>(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return null;
                }

                var symbol = await SymbolFinder
                    .FindSymbolAtPositionAsync(document, Math.Clamp(offset, 0, text.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (symbol is null || !symbol.Locations.Any(location => location.IsInSource))
                {
                    return null;
                }

                var solution = document.Project.Solution;
                var renamedSolution = await Renamer
                    .RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, cancellationToken)
                    .ConfigureAwait(false);

                var changes = new List<CoreDocuments.FileTextChange>();
                foreach (var projectChanges in renamedSolution.GetChanges(solution).GetProjectChanges())
                {
                    foreach (var documentId in projectChanges.GetChangedDocuments())
                    {
                        var changedDocument = renamedSolution.GetDocument(documentId);
                        if (changedDocument?.FilePath is not { } changedPath)
                        {
                            continue;
                        }

                        var newText = await changedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                        changes.Add(new CoreDocuments.FileTextChange(changedPath, newText.ToString()));
                    }
                }

                // Keep the renamed solution so subsequent queries see the new names.
                _solution = renamedSolution;
                return changes;
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves the symbol at the offset and runs <paramref name="query"/> under the workspace gate.</summary>
    private async Task<IReadOnlyList<SearchMatch>> RunSymbolQueryAsync(
        string filePath,
        string text,
        int offset,
        Func<ISymbol, Solution, CancellationToken, Task<IReadOnlyList<SearchMatch>>> query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        return await Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var document = UpdateAndGetDocument(filePath, text);
                if (document is null)
                {
                    return [];
                }

                var symbol = await SymbolFinder
                    .FindSymbolAtPositionAsync(document, Math.Clamp(offset, 0, text.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (symbol is null)
                {
                    return [];
                }

                return await query(symbol, document.Project.Solution, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds a display match (line text, windowed) for a source location.</summary>
    private static async Task<SearchMatch?> MapLocationAsync(Location location, CancellationToken cancellationToken)
    {
        var tree = location.SourceTree;
        if (tree?.FilePath is not { Length: > 0 } path)
        {
            return null;
        }

        var sourceText = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var lineIndex = location.GetLineSpan().StartLinePosition.Line;
        if (lineIndex >= sourceText.Lines.Count)
        {
            return null;
        }

        var line = sourceText.Lines[lineIndex];
        var lineText = line.ToString();
        var startInLine = Math.Clamp(location.SourceSpan.Start - line.Start, 0, lineText.Length);
        var length = Math.Clamp(location.SourceSpan.Length, 1, Math.Max(1, lineText.Length - startInLine));
        return SearchMatchFactory.Create(path, lineIndex + 1, lineText, startInLine, length);
    }

    private static List<SearchMatch> SortMatches(List<SearchMatch> matches)
    {
        matches.Sort((left, right) =>
        {
            var byPath = string.Compare(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
            return byPath != 0 ? byPath : left.LineNumber.CompareTo(right.LineNumber);
        });
        return matches;
    }

    /// <summary>
    /// Forks the current solution with <paramref name="text"/> for the file and keeps
    /// it, so edits accumulate across calls. Must be called while holding the gate.
    /// Returns null when the file is not part of the loaded workspace.
    /// </summary>
    private Document? UpdateAndGetDocument(string filePath, string text)
    {
        var solution = _solution;
        var documentIds = solution?.GetDocumentIdsWithFilePath(filePath);
        if (solution is null || documentIds is not [var documentId, ..])
        {
            return null;
        }

        solution = solution.WithDocumentText(documentId, SourceText.From(text));
        _solution = solution;
        return solution.GetDocument(documentId);
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _workspace = null;
        _solution = null;
        _gate.Dispose();
    }

    private static bool TryRegisterMsBuild(out string unavailableReason)
    {
        lock (MsBuildRegistrationLock)
        {
            if (!_msBuildRegistrationAttempted)
            {
                _msBuildRegistrationAttempted = true;
                try
                {
                    if (!MSBuildLocator.IsRegistered)
                    {
                        MSBuildLocator.RegisterDefaults();
                    }

                    _msBuildAvailable = true;
                }
                catch (InvalidOperationException)
                {
                    _msBuildAvailable = false;
                }
            }

            unavailableReason = _msBuildAvailable ? string.Empty : "no .NET SDK found.";
            return _msBuildAvailable;
        }
    }

    /// <summary>Finds a solution in the root (or one level down), else up to 20 projects.</summary>
    private (string? SolutionPath, List<string> ProjectPaths) FindLoadTarget(string rootPath)
    {
        var searchDirectories = new List<string> { rootPath };
        searchDirectories.AddRange(Directory
            .EnumerateDirectories(rootPath)
            .Where(directory => !IsExcluded(Path.GetFileName(directory))));

        foreach (var pattern in (string[])["*.sln", "*.slnx"])
        {
            foreach (var directory in searchDirectories)
            {
                var solution = Directory
                    .EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (solution is not null)
                {
                    return (solution, []);
                }
            }
        }

        var projects = new List<string>();
        CollectProjects(rootPath, projects);
        return (null, projects);
    }

    private void CollectProjects(string directory, List<string> projects)
    {
        if (projects.Count >= MaxProjectsWithoutSolution)
        {
            return;
        }

        projects.AddRange(Directory
            .EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly)
            .Take(MaxProjectsWithoutSolution - projects.Count));

        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            if (!IsExcluded(Path.GetFileName(subdirectory)))
            {
                CollectProjects(subdirectory, projects);
            }
        }
    }

    private async Task OpenAsync(string? solutionPath, List<string> projectPaths, CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.SkipUnrecognizedProjects = true;
        workspace.WorkspaceFailed += (_, e) =>
            _logger.LogWarning("C# workspace load issue ({Kind}): {Message}", e.Diagnostic.Kind, e.Diagnostic.Message);
        _workspace = workspace;

        if (solutionPath is not null)
        {
            try
            {
                await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken).ConfigureAwait(false);
                _solution = workspace.CurrentSolution;
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Whatever MSBuild throws for a solution it cannot parse (e.g. .slnx
                // on Roslyn versions without support) must fall back to projects.
                _logger.LogWarning(ex, "Could not open solution {Path}; falling back to project scan", solutionPath);
                var root = Path.GetDirectoryName(solutionPath)!;
                projectPaths = [];
                CollectProjects(root, projectPaths);
            }
        }

        foreach (var projectPath in projectPaths)
        {
            // Opening a project pulls in its project references, so later list
            // entries may already be loaded.
            var alreadyLoaded = workspace.CurrentSolution.Projects.Any(project =>
                string.Equals(project.FilePath, projectPath, StringComparison.OrdinalIgnoreCase));
            if (alreadyLoaded)
            {
                continue;
            }

            try
            {
                await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best effort: one broken project must not sink the rest.
                _logger.LogWarning(ex, "Could not open project {Path}", projectPath);
            }
        }

        _solution = workspace.CurrentSolution;
    }

    private void UnloadCore(string? status)
    {
        _workspace?.Dispose();
        _workspace = null;
        _solution = null;
        _lastCompletion = null;
        if (status is not null)
        {
            Report(status);
        }
    }

    private static IReadOnlyList<CoreDiagnostics.DiagnosticItem> ParseLooseFile(
        string filePath, string text, CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath, cancellationToken: cancellationToken);
        return Map(tree.GetDiagnostics(cancellationToken), filePath);
    }

    private static List<CoreDiagnostics.DiagnosticItem> Map(IEnumerable<Diagnostic> diagnostics, string fallbackPath)
    {
        var mapped = new List<CoreDiagnostics.DiagnosticItem>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Hidden || diagnostic.IsSuppressed)
            {
                continue;
            }

            var span = diagnostic.Location.GetLineSpan();
            var path = span.Path is { Length: > 0 } sourcePath ? sourcePath : fallbackPath;
            mapped.Add(new CoreDiagnostics.DiagnosticItem(
                diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => CoreDiagnostics.DiagnosticSeverity.Error,
                    DiagnosticSeverity.Warning => CoreDiagnostics.DiagnosticSeverity.Warning,
                    _ => CoreDiagnostics.DiagnosticSeverity.Info,
                },
                $"{diagnostic.Id}: {diagnostic.GetMessage()}",
                path,
                span.IsValid ? span.StartLinePosition.Line + 1 : 0,
                span.IsValid ? span.StartLinePosition.Character + 1 : 0,
                "csharp",
                span.IsValid ? diagnostic.Location.SourceSpan.Length : 0));

            if (mapped.Count >= MaxDiagnosticsPerFile)
            {
                break;
            }
        }

        return mapped;
    }

    private bool IsExcluded(string? folderName)
        => folderName is not null
           && _settingsService.Settings.ExplorerExcludedFolders
               .Contains(folderName, StringComparer.OrdinalIgnoreCase);

    private void Report(string status)
    {
        _logger.LogInformation("{Status}", status);
        StatusChanged?.Invoke(this, status);
    }
}
