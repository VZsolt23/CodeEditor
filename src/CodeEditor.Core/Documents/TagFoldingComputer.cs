namespace CodeEditor.Core.Documents;

/// <summary>
/// Computes fold regions for HTML/XML: matched element pairs and comments that
/// span multiple lines. A tolerant single-pass scanner, not a validator — quoted
/// attributes, CDATA, comments, and declarations are skipped correctly, unclosed
/// tags are dropped, and stray closing tags are ignored, so folding stays useful
/// while the document is mid-edit.
/// </summary>
public static class TagFoldingComputer
{
    /// <summary>
    /// Returns fold regions in no particular order. An element folds
    /// <c>[end of its open tag, start of its close tag)</c> so a collapsed
    /// element renders as <c>&lt;div&gt;…&lt;/div&gt;</c>; a comment folds its interior
    /// (<c>&lt;!--…--&gt;</c>). In HTML mode tag names match case-insensitively and
    /// void elements (<c>&lt;br&gt;</c> etc.) never open a fold.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> ComputeTagFolds(string text, bool isHtml)
    {
        ArgumentNullException.ThrowIfNull(text);

        var folds = new List<(int Start, int End)>();
        var openTags = new List<(string Name, int ContentStart)>(); // A list, not a stack: mismatch recovery searches it.
        var comparison = isHtml ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var i = 0;
        while ((i = text.IndexOf('<', i)) >= 0)
        {
            if (Starts(text, i, "<!--"))
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                AddIfMultiLine(folds, text, i + 4, end);
                i = end + 3;
            }
            else if (Starts(text, i, "<![CDATA["))
            {
                var end = text.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                i = end + 3;
            }
            else if (Starts(text, i, "<!") || Starts(text, i, "<?"))
            {
                var end = text.IndexOf('>', i + 2);
                if (end < 0)
                {
                    break;
                }

                i = end + 1;
            }
            else if (Starts(text, i, "</"))
            {
                var name = ReadName(text, i + 2);
                var end = text.IndexOf('>', i + 2);
                if (end < 0)
                {
                    break;
                }

                // Match the innermost open tag of this name; anything opened above
                // it was never closed and is discarded (typical mid-edit state).
                for (var depth = openTags.Count - 1; depth >= 0; depth--)
                {
                    if (string.Equals(openTags[depth].Name, name, comparison))
                    {
                        AddIfMultiLine(folds, text, openTags[depth].ContentStart, i);
                        openTags.RemoveRange(depth, openTags.Count - depth);
                        break;
                    }
                }

                i = end + 1;
            }
            else
            {
                var name = ReadName(text, i + 1);
                if (name.Length == 0)
                {
                    i++; // A bare '<' (e.g. "a < b" in HTML text content).
                    continue;
                }

                var end = FindTagEnd(text, i + 1 + name.Length);
                if (end < 0)
                {
                    break;
                }

                var selfClosing = end > 0 && text[end - 1] == '/';
                if (!selfClosing && !(isHtml && TagAutoCloser.IsVoidElement(name)))
                {
                    openTags.Add((name, end + 1));
                }

                i = end + 1;
            }
        }

        return folds;
    }

    private static void AddIfMultiLine(List<(int Start, int End)> folds, string text, int start, int end)
    {
        if (start >= end)
        {
            return;
        }

        for (var i = start; i < end; i++)
        {
            if (text[i] == '\n')
            {
                folds.Add((start, end));
                return;
            }
        }
    }

    private static bool Starts(string text, int offset, string prefix)
        => string.CompareOrdinal(text, offset, prefix, 0, prefix.Length) == 0;

    private static string ReadName(string text, int offset)
    {
        if (offset >= text.Length || !(char.IsLetter(text[offset]) || text[offset] is '_' or ':'))
        {
            return string.Empty;
        }

        var end = offset;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '-' or '_' or ':' or '.'))
        {
            end++;
        }

        return text[offset..end];
    }

    /// <summary>Finds the tag's closing '&gt;', skipping quoted attribute values (which may contain '&gt;').</summary>
    private static int FindTagEnd(string text, int offset)
    {
        var i = offset;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '>')
            {
                return i;
            }

            if (c is '"' or '\'')
            {
                var close = text.IndexOf(c, i + 1);
                if (close < 0)
                {
                    return -1;
                }

                i = close + 1;
            }
            else
            {
                i++;
            }
        }

        return -1;
    }
}
