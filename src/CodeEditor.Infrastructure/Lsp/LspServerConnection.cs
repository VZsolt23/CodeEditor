using System.Text.Json;
using CodeEditor.Core.Diagnostics;
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
