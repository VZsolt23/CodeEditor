namespace CodeEditor.Core.Documents;

/// <summary>
/// One classified span of a document, produced by semantic classification
/// (e.g. Roslyn's classifier) and consumed by the editor's semantic colorizer.
/// </summary>
/// <param name="Start">0-based start offset.</param>
/// <param name="Length">Span length in characters.</param>
/// <param name="Classification">Classification name (e.g. "keyword", "class name", "method name").</param>
public sealed record ClassifiedSpanInfo(int Start, int Length, string Classification);
