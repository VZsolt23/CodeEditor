namespace CodeEditor.Core.Documents;

/// <summary>
/// A contiguous character range inside a text buffer.
/// </summary>
/// <param name="Start">0-based start offset.</param>
/// <param name="Length">Length in characters.</param>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>Offset of the first character after the span.</summary>
    public int End => Start + Length;
}
