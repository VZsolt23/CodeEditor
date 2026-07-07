using System.Text;

namespace CodeEditor.Core.Documents;

/// <summary>
/// Applies LSP range edits (0-based line/character) to text, producing the new
/// full content. Positions are resolved against the given text, so the caller
/// must apply edits to the same content the server computed them from.
/// </summary>
public static class TextEditApplier
{
    /// <summary>Applies <paramref name="edits"/> to <paramref name="text"/>. Edits must not overlap.</summary>
    public static string Apply(string text, IReadOnlyList<LspRangeEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edits);

        if (edits.Count == 0)
        {
            return text;
        }

        var lineStarts = ComputeLineStarts(text);

        var spans = new List<(int Start, int End, string NewText)>(edits.Count);
        foreach (var edit in edits)
        {
            var start = ToOffset(text, lineStarts, edit.StartLine, edit.StartChar);
            var end = Math.Max(start, ToOffset(text, lineStarts, edit.EndLine, edit.EndChar));
            spans.Add((start, end, edit.NewText));
        }

        // Apply back-to-front so earlier offsets stay valid.
        spans.Sort(static (left, right) => right.Start.CompareTo(left.Start));

        var builder = new StringBuilder(text);
        foreach (var (start, end, newText) in spans)
        {
            var clampedEnd = Math.Clamp(end, 0, builder.Length);
            var clampedStart = Math.Clamp(start, 0, clampedEnd);
            builder.Remove(clampedStart, clampedEnd - clampedStart);
            builder.Insert(clampedStart, newText);
        }

        return builder.ToString();
    }

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    private static int ToOffset(string text, int[] lineStarts, int line, int character)
    {
        if (line < 0)
        {
            return 0;
        }

        if (line >= lineStarts.Length)
        {
            return text.Length;
        }

        var lineStart = lineStarts[line];
        var lineEnd = line + 1 < lineStarts.Length ? lineStarts[line + 1] : text.Length;
        return Math.Clamp(lineStart + character, lineStart, lineEnd);
    }
}
