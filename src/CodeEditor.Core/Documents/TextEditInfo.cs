namespace CodeEditor.Core.Documents;

/// <summary>
/// One span replacement inside a document, produced by operations like Format
/// Document. Apply a batch back-to-front so earlier offsets stay valid.
/// </summary>
/// <param name="Start">0-based offset of the replaced span.</param>
/// <param name="Length">Length of the replaced span.</param>
/// <param name="NewText">Replacement text.</param>
public sealed record TextEditInfo(int Start, int Length, string NewText);
