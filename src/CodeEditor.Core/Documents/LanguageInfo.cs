namespace CodeEditor.Core.Documents;

/// <summary>
/// Describes a language recognized by the editor.
/// </summary>
/// <param name="Id">Stable identifier (e.g. "csharp", "typescript"). Used to key language services.</param>
/// <param name="DisplayName">Human-readable name shown in the UI (e.g. status bar).</param>
/// <param name="Extensions">File extensions (including the leading dot) associated with the language.</param>
public sealed record LanguageInfo(string Id, string DisplayName, IReadOnlyList<string> Extensions)
{
    /// <summary>Fallback language used for files the editor does not recognize.</summary>
    public static LanguageInfo PlainText { get; } = new("plaintext", "Plain Text", [".txt"]);
}
