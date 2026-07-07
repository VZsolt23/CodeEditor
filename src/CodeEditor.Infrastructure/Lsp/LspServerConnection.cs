using System.Text.Json;
using System.Text.RegularExpressions;
using CodeEditor.Core.Completion;
using CodeEditor.Core.Diagnostics;
using CodeEditor.Core.Documents;
using CodeEditor.Core.Workspace;
using StreamJsonRpc;

namespace CodeEditor.Infrastructure.Lsp;

/// <summary>
/// One JSON-RPC connection to a language server (LSP over stdio-style streams).
/// Stream-based rather than process-based so the protocol layer is testable
/// against an in-process server. Handles the initialize handshake, full-text
/// document synchronization, and maps published diagnostics to
/// <see cref="DiagnosticItem"/>s (LSP is 0-based; ours is 1-based).
/// </summary>
public sealed class LspServerConnection : IDisposable
{
    private const int MaxDiagnosticsPerFile = 500;
    private const int MaxCompletionItems = 300;

    private readonly JsonRpc _rpc;
    private readonly string _diagnosticSource;
    private readonly Action<string, IReadOnlyList<DiagnosticItem>> _onDiagnostics;
    private readonly Dictionary<string, int> _documentVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _versionLock = new();

    private LspServerConnection(
        JsonRpc rpc, string diagnosticSource, Action<string, IReadOnlyList<DiagnosticItem>> onDiagnostics)
    {
        _rpc = rpc;
        _diagnosticSource = diagnosticSource;
        _onDiagnostics = onDiagnostics;
    }

    /// <summary>
    /// Connects over the given streams and performs the initialize handshake.
    /// </summary>
    /// <param name="writeTo">Stream carrying client→server messages (the server's stdin).</param>
    /// <param name="readFrom">Stream carrying server→client messages (the server's stdout).</param>
    /// <param name="rootPath">Workspace root announced to the server.</param>
    /// <param name="diagnosticSource">Source label stamped on produced <see cref="DiagnosticItem"/>s.</param>
    /// <param name="onDiagnostics">Called (on RPC threads) with the file path and mapped diagnostics.</param>
    public static async Task<LspServerConnection> CreateAsync(
        Stream writeTo,
        Stream readFrom,
        string rootPath,
        string diagnosticSource,
        Action<string, IReadOnlyList<DiagnosticItem>> onDiagnostics,
        CancellationToken cancellationToken = default)
    {
        var handler = new HeaderDelimitedMessageHandler(writeTo, readFrom, new SystemTextJsonFormatter());
        var rpc = new JsonRpc(handler);
        var connection = new LspServerConnection(rpc, diagnosticSource, onDiagnostics);
        rpc.AddLocalRpcTarget(new NotificationTarget(connection));
        rpc.StartListening();

        await rpc.InvokeWithParameterObjectAsync<JsonElement>(
            "initialize",
            new
            {
                processId = Environment.ProcessId,
                rootUri = ToUri(rootPath),
                capabilities = new
                {
                    textDocument = new
                    {
                        publishDiagnostics = new { },
                        synchronization = new { dynamicRegistration = false, didSave = false },
                    },
                },
            },
            cancellationToken).ConfigureAwait(false);
        await rpc.NotifyWithParameterObjectAsync("initialized", new { }).ConfigureAwait(false);

        return connection;
    }

    /// <summary>Sends textDocument/didOpen and starts version tracking for the file.</summary>
    public async Task OpenDocumentAsync(string filePath, string languageId, string text)
    {
        lock (_versionLock)
        {
            _documentVersions[filePath] = 1;
        }

        await _rpc.NotifyWithParameterObjectAsync(
            "textDocument/didOpen",
            new { textDocument = new { uri = ToUri(filePath), languageId, version = 1, text } })
            .ConfigureAwait(false);
    }

    /// <summary>Sends the full new text via textDocument/didChange; ignored for unopened files.</summary>
    public async Task ChangeDocumentAsync(string filePath, string text)
    {
        int version;
        lock (_versionLock)
        {
            if (!_documentVersions.TryGetValue(filePath, out var current))
            {
                return;
            }

            version = _documentVersions[filePath] = current + 1;
        }

        await _rpc.NotifyWithParameterObjectAsync(
            "textDocument/didChange",
            new
            {
                textDocument = new { uri = ToUri(filePath), version },
                contentChanges = new[] { new { text } },
            })
            .ConfigureAwait(false);
    }

    /// <summary>Sends textDocument/didClose.</summary>
    public async Task CloseDocumentAsync(string filePath)
    {
        lock (_versionLock)
        {
            if (!_documentVersions.Remove(filePath))
            {
                return;
            }
        }

        await _rpc.NotifyWithParameterObjectAsync(
            "textDocument/didClose",
            new { textDocument = new { uri = ToUri(filePath) } })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Requests hover text at a 0-based <paramref name="line"/>/<paramref name="character"/>
    /// position, returning plain text (markdown fences stripped) or null when the
    /// server has nothing to show.
    /// </summary>
    public async Task<string?> RequestHoverAsync(
        string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
            "textDocument/hover",
            new { textDocument = new { uri = ToUri(filePath) }, position = new { line, character } },
            cancellationToken).ConfigureAwait(false);

        return result is { } element ? ExtractHoverText(element) : null;
    }

    /// <summary>
    /// Requests completions at a 0-based <paramref name="line"/>/<paramref name="character"/>
    /// position. Each item carries its own insert text (textEdit/insertText/label,
    /// snippet placeholders stripped). Returns null when the server offers nothing.
    /// </summary>
    public async Task<IReadOnlyList<CompletionItemInfo>?> RequestCompletionAsync(
        string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
            "textDocument/completion",
            new { textDocument = new { uri = ToUri(filePath) }, position = new { line, character } },
            cancellationToken).ConfigureAwait(false);

        if (result is not { } element)
        {
            return null;
        }

        // The reply is either a CompletionItem[] or a CompletionList { items: [...] }.
        JsonElement items;
        if (element.ValueKind == JsonValueKind.Array)
        {
            items = element;
        }
        else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("items", out var listItems))
        {
            items = listItems;
        }
        else
        {
            return null;
        }

        var mapped = new List<CompletionItemInfo>();
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var label = GetString(item, "label");
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            mapped.Add(new CompletionItemInfo(
                index++,
                label,
                GetString(item, "filterText") is { Length: > 0 } filter ? filter : label,
                GetString(item, "sortText") is { Length: > 0 } sort ? sort : label,
                MapCompletionKind(item),
                ComputeInsertText(item, label)));

            if (mapped.Count >= MaxCompletionItems)
            {
                break;
            }
        }

        return mapped.Count > 0 ? mapped : null;
    }

    /// <summary>
    /// Requests the definition(s) of the symbol at a 0-based
    /// <paramref name="line"/>/<paramref name="character"/> position. Handles the
    /// Location, Location[], and LocationLink[] reply shapes; excerpts are read from
    /// the target files. Empty when the server resolves nothing.
    /// </summary>
    public async Task<IReadOnlyList<SearchMatch>> RequestDefinitionAsync(
        string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
            "textDocument/definition",
            new { textDocument = new { uri = ToUri(filePath) }, position = new { line, character } },
            cancellationToken).ConfigureAwait(false);

        if (result is not { } element)
        {
            return [];
        }

        var matches = new List<SearchMatch>();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var location in element.EnumerateArray())
            {
                AddLocation(location, matches);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            AddLocation(element, matches);
        }

        return matches;
    }

    /// <summary>
    /// Requests all references (including the declaration) of the symbol at a 0-based
    /// <paramref name="line"/>/<paramref name="character"/> position, sorted by file
    /// then line. Empty when the server resolves nothing.
    /// </summary>
    public async Task<IReadOnlyList<SearchMatch>> RequestReferencesAsync(
        string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
            "textDocument/references",
            new
            {
                textDocument = new { uri = ToUri(filePath) },
                position = new { line, character },
                context = new { includeDeclaration = true },
            },
            cancellationToken).ConfigureAwait(false);

        if (result is not { } element || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var matches = new List<SearchMatch>();
        foreach (var location in element.EnumerateArray())
        {
            AddLocation(location, matches);
        }

        matches.Sort(static (left, right) =>
        {
            var byPath = string.Compare(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
            return byPath != 0 ? byPath : left.LineNumber.CompareTo(right.LineNumber);
        });
        return matches;
    }

    /// <summary>
    /// Renames the symbol at a 0-based <paramref name="line"/>/<paramref name="character"/>
    /// position to <paramref name="newName"/>. Returns the per-file range edits from
    /// the WorkspaceEdit (both the <c>changes</c> map and <c>documentChanges</c> array
    /// shapes; resource operations are ignored), or null when nothing changes.
    /// </summary>
    public async Task<IReadOnlyList<LspFileEdits>?> RequestRenameAsync(
        string filePath, int line, int character, string newName, CancellationToken cancellationToken = default)
    {
        var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
            "textDocument/rename",
            new { textDocument = new { uri = ToUri(filePath) }, position = new { line, character }, newName },
            cancellationToken).ConfigureAwait(false);

        if (result is not { } edit || edit.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var files = new List<LspFileEdits>();

        if (edit.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in changes.EnumerateObject())
            {
                AddFileEdits(entry.Name, entry.Value, files);
            }
        }
        else if (edit.TryGetProperty("documentChanges", out var documentChanges) && documentChanges.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in documentChanges.EnumerateArray())
            {
                // Skip resource operations (create/rename/delete file) — no "edits".
                if (change.TryGetProperty("textDocument", out var textDocument)
                    && change.TryGetProperty("edits", out var edits)
                    && GetString(textDocument, "uri") is { } uri)
                {
                    AddFileEdits(uri, edits, files);
                }
            }
        }

        return files.Count > 0 ? files : null;
    }

    private static void AddFileEdits(string uri, JsonElement edits, List<LspFileEdits> files)
    {
        string path;
        try
        {
            path = new Uri(uri).LocalPath;
        }
        catch (UriFormatException)
        {
            return;
        }

        if (edits.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var parsed = new List<LspRangeEdit>();
        foreach (var edit in edits.EnumerateArray())
        {
            if (!edit.TryGetProperty("range", out var range)
                || !range.TryGetProperty("start", out var start)
                || !range.TryGetProperty("end", out var end))
            {
                continue;
            }

            parsed.Add(new LspRangeEdit(
                start.GetProperty("line").GetInt32(),
                start.GetProperty("character").GetInt32(),
                end.GetProperty("line").GetInt32(),
                end.GetProperty("character").GetInt32(),
                GetString(edit, "newText") ?? string.Empty));
        }

        if (parsed.Count > 0)
        {
            files.Add(new LspFileEdits(path, parsed));
        }
    }

    /// <summary>Performs the polite shutdown/exit sequence, tolerating a dead server.</summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _rpc.InvokeWithCancellationAsync<JsonElement?>("shutdown", arguments: null, cancellationToken).ConfigureAwait(false);
            await _rpc.NotifyAsync("exit").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RemoteRpcException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The server is already gone or unresponsive; the process gets killed anyway.
        }
    }

    public void Dispose() => _rpc.Dispose();

    private void HandleDiagnostics(PublishDiagnosticsParams parameters)
    {
        string filePath;
        try
        {
            filePath = new Uri(parameters.Uri).LocalPath;
        }
        catch (UriFormatException)
        {
            return;
        }

        var mapped = new List<DiagnosticItem>();
        foreach (var diagnostic in parameters.Diagnostics)
        {
            mapped.Add(Map(diagnostic, filePath));
            if (mapped.Count >= MaxDiagnosticsPerFile)
            {
                break;
            }
        }

        _onDiagnostics(filePath, mapped);
    }

    private DiagnosticItem Map(LspDiagnostic diagnostic, string filePath)
    {
        var start = diagnostic.Range.Start;
        var end = diagnostic.Range.End;
        var length = end.Line == start.Line ? Math.Max(0, end.Character - start.Character) : 0;

        var code = diagnostic.Code is { } codeElement && codeElement.ValueKind is not JsonValueKind.Null
            ? codeElement.ToString()
            : null;
        var message = string.IsNullOrEmpty(code) ? diagnostic.Message : $"{code}: {diagnostic.Message}";

        return new DiagnosticItem(
            diagnostic.Severity switch
            {
                1 => DiagnosticSeverity.Error,
                2 => DiagnosticSeverity.Warning,
                4 => DiagnosticSeverity.Hint,
                _ => DiagnosticSeverity.Info,
            },
            message,
            filePath,
            start.Line + 1,
            start.Character + 1,
            _diagnosticSource,
            length);
    }

    /// <summary>Extracts text from a hover result's <c>contents</c> (string, MarkupContent, MarkedString, or an array).</summary>
    private static string? ExtractHoverText(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("contents", out var contents))
        {
            return null;
        }

        var text = CleanMarkup(ExtractContents(contents));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string ExtractContents(JsonElement contents) => contents.ValueKind switch
    {
        JsonValueKind.String => contents.GetString() ?? string.Empty,
        JsonValueKind.Object => contents.TryGetProperty("value", out var value) ? value.GetString() ?? string.Empty : string.Empty,
        JsonValueKind.Array => string.Join(
            Environment.NewLine,
            contents.EnumerateArray().Select(ExtractContents).Where(part => part.Length > 0)),
        _ => string.Empty,
    };

    /// <summary>Drops markdown code-fence lines so the plain hover reads cleanly in the tooltip.</summary>
    private static string CleanMarkup(string markdown)
    {
        var lines = markdown
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal));
        return string.Join(Environment.NewLine, lines).Trim();
    }

    /// <summary>Resolves an item's commit text: textEdit.newText, else insertText, else label; snippets flattened.</summary>
    private static string ComputeInsertText(JsonElement item, string label)
    {
        string text;
        if (item.TryGetProperty("textEdit", out var textEdit)
            && textEdit.ValueKind == JsonValueKind.Object
            && GetString(textEdit, "newText") is { Length: > 0 } editText)
        {
            text = editText;
        }
        else if (GetString(item, "insertText") is { Length: > 0 } insertText)
        {
            text = insertText;
        }
        else
        {
            text = label;
        }

        // insertTextFormat 2 = snippet; reduce placeholders to plain text.
        var isSnippet = item.TryGetProperty("insertTextFormat", out var format)
            && format.ValueKind == JsonValueKind.Number
            && format.GetInt32() == 2;
        return isSnippet ? FlattenSnippet(text) : text;
    }

    private static string FlattenSnippet(string snippet)
    {
        var text = Regex.Replace(snippet, @"\$\{\d+:([^}]*)\}", "$1"); // ${1:name} -> name
        text = Regex.Replace(text, @"\$\{\d+\}", string.Empty);        // ${1} -> ""
        text = Regex.Replace(text, @"\$\d+", string.Empty);            // $1, $0 -> ""
        return text.Replace("\\$", "$").Replace("\\}", "}");
    }

    /// <summary>Maps the LSP CompletionItemKind enum (1-25) to a short display tag.</summary>
    private static string MapCompletionKind(JsonElement item)
    {
        if (!item.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.Number)
        {
            return string.Empty;
        }

        return kind.GetInt32() switch
        {
            2 => "Method",
            3 => "Function",
            4 => "Constructor",
            5 => "Field",
            6 => "Variable",
            7 => "Class",
            8 => "Interface",
            9 => "Module",
            10 => "Property",
            13 => "Enum",
            14 => "Keyword",
            15 => "Snippet",
            20 => "Enum member",
            21 => "Constant",
            22 => "Struct",
            25 => "Type parameter",
            _ => string.Empty,
        };
    }

    /// <summary>Maps one LSP Location or LocationLink to a <see cref="SearchMatch"/> (with a disk-read excerpt).</summary>
    private static void AddLocation(JsonElement location, List<SearchMatch> matches)
    {
        var uri = GetString(location, "uri") ?? GetString(location, "targetUri");
        if (uri is null)
        {
            return;
        }

        JsonElement range;
        if (!location.TryGetProperty("range", out range)
            && !location.TryGetProperty("targetSelectionRange", out range)
            && !location.TryGetProperty("targetRange", out range))
        {
            return;
        }

        if (range.ValueKind != JsonValueKind.Object || !range.TryGetProperty("start", out var start))
        {
            return;
        }

        string path;
        try
        {
            path = new Uri(uri).LocalPath;
        }
        catch (UriFormatException)
        {
            return;
        }

        var lineIndex = start.GetProperty("line").GetInt32();
        var startChar = start.GetProperty("character").GetInt32();
        var endChar = range.TryGetProperty("end", out var end) && end.GetProperty("line").GetInt32() == lineIndex
            ? end.GetProperty("character").GetInt32()
            : startChar;

        var lineText = ReadLine(path, lineIndex) ?? string.Empty;
        var clampedStart = Math.Clamp(startChar, 0, lineText.Length);
        var length = Math.Clamp(endChar - startChar, 1, Math.Max(1, lineText.Length - clampedStart));
        matches.Add(SearchMatchFactory.Create(path, lineIndex + 1, lineText, clampedStart, length));
    }

    /// <summary>Reads a single 0-based line from a file, or null when it cannot be read.</summary>
    private static string? ReadLine(string path, int lineIndex)
    {
        try
        {
            return File.ReadLines(path).Skip(lineIndex).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ToUri(string path) => new Uri(path).AbsoluteUri;

    /// <summary>Server→client notifications we care about (the rest are ignored or rejected).</summary>
    private sealed class NotificationTarget(LspServerConnection connection)
    {
        [JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization = true)]
        public void PublishDiagnostics(PublishDiagnosticsParams parameters) => connection.HandleDiagnostics(parameters);

        [JsonRpcMethod("window/logMessage", UseSingleObjectParameterDeserialization = true)]
        public void LogMessage(JsonElement parameters)
        {
            // Server log chatter; intentionally dropped.
        }

        [JsonRpcMethod("window/showMessage", UseSingleObjectParameterDeserialization = true)]
        public void ShowMessage(JsonElement parameters)
        {
            // Not surfaced yet.
        }
    }
}
