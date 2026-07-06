using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeEditor.Infrastructure.Lsp;

/// <summary>Payload of the textDocument/publishDiagnostics notification.</summary>
internal sealed class PublishDiagnosticsParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("diagnostics")]
    public List<LspDiagnostic> Diagnostics { get; set; } = [];
}

internal sealed class LspDiagnostic
{
    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    /// <summary>1=Error, 2=Warning, 3=Information, 4=Hint.</summary>
    [JsonPropertyName("severity")]
    public int? Severity { get; set; }

    /// <summary>Number or string per the spec.</summary>
    [JsonPropertyName("code")]
    public JsonElement? Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

internal sealed class LspRange
{
    [JsonPropertyName("start")]
    public LspPosition Start { get; set; } = new();

    [JsonPropertyName("end")]
    public LspPosition End { get; set; } = new();
}

/// <summary>0-based line/character position.</summary>
internal sealed class LspPosition
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("character")]
    public int Character { get; set; }
}
