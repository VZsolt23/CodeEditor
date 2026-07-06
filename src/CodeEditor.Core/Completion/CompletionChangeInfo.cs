namespace CodeEditor.Core.Completion;

/// <summary>
/// The text edit that commits a completion item.
/// </summary>
/// <param name="Start">0-based offset of the replaced span (against the text the list was computed for).</param>
/// <param name="Length">Length of the replaced span.</param>
/// <param name="NewText">Replacement text.</param>
public sealed record CompletionChangeInfo(int Start, int Length, string NewText);
