namespace CodeEditor.Core.Documents;

/// <summary>
/// Computes brace-based fold regions (<c>{ … }</c>) for the editor. Nesting is
/// tracked with a stack; only regions spanning more than one line are returned
/// (single-line braces aren't worth folding). Braces are counted literally (no
/// string/comment awareness) — the common lightweight behavior.
/// </summary>
public static class FoldingComputer
{
    /// <summary>
    /// Returns fold regions as <c>[openBraceOffset, closeBraceOffset + 1)</c>, in
    /// document order (innermost regions close first).
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> ComputeBraceFolds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var folds = new List<(int Start, int End)>();
        var openOffsets = new Stack<int>();

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    openOffsets.Push(i);
                    break;
                case '}' when openOffsets.Count > 0:
                    var start = openOffsets.Pop();
                    if (SpansMultipleLines(text, start + 1, i))
                    {
                        folds.Add((start, i + 1));
                    }

                    break;
            }
        }

        return folds;
    }

    private static bool SpansMultipleLines(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (text[i] == '\n')
            {
                return true;
            }
        }

        return false;
    }
}
