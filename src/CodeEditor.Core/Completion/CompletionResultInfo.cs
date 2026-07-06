namespace CodeEditor.Core.Completion;

/// <summary>
/// A completion list for one caret position.
/// </summary>
/// <param name="ReplacementStart">0-based offset of the span the completion replaces (the partially typed word).</param>
/// <param name="ReplacementLength">Length of the replaced span at request time.</param>
/// <param name="Items">Suggestions in provider order.</param>
public sealed record CompletionResultInfo(int ReplacementStart, int ReplacementLength, IReadOnlyList<CompletionItemInfo> Items);
