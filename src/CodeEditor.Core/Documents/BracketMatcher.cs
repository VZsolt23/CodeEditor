namespace CodeEditor.Core.Documents;

/// <summary>
/// Finds the matching bracket for the one next to a caret. Works over a character
/// accessor so the editor can scan its rope without copying the whole document.
/// Counts brackets literally (no string/comment awareness), which matches the
/// common lightweight bracket-matcher behavior.
/// </summary>
public static class BracketMatcher
{
    private const string Openers = "([{";
    private const string Closers = ")]}";

    // Cap the scan so a caret next to a bracket whose partner is very far away
    // (or missing) cannot cost an unbounded walk on every caret move.
    private const int MaxScan = 100_000;

    /// <summary>
    /// Returns the (open, close) offsets of the bracket pair to highlight for a caret
    /// at <paramref name="caret"/>, or null when the caret is not next to a matched
    /// bracket. The bracket immediately before the caret is preferred, then the one at it.
    /// </summary>
    public static (int Open, int Close)? Match(int length, Func<int, char> charAt, int caret)
    {
        ArgumentNullException.ThrowIfNull(charAt);

        if (caret > 0 && caret <= length && TryMatchAt(length, charAt, caret - 1) is { } before)
        {
            return before;
        }

        if (caret >= 0 && caret < length && TryMatchAt(length, charAt, caret) is { } at)
        {
            return at;
        }

        return null;
    }

    private static (int Open, int Close)? TryMatchAt(int length, Func<int, char> charAt, int position)
    {
        var bracket = charAt(position);

        var openIndex = Openers.IndexOf(bracket);
        if (openIndex >= 0)
        {
            return ScanForward(length, charAt, position, bracket, Closers[openIndex]);
        }

        var closeIndex = Closers.IndexOf(bracket);
        if (closeIndex >= 0)
        {
            return ScanBackward(charAt, position, Openers[closeIndex], bracket);
        }

        return null;
    }

    private static (int, int)? ScanForward(int length, Func<int, char> charAt, int start, char open, char close)
    {
        var depth = 0;
        var end = Math.Min(length, start + MaxScan);
        for (var i = start; i < end; i++)
        {
            var c = charAt(i);
            if (c == open)
            {
                depth++;
            }
            else if (c == close && --depth == 0)
            {
                return (start, i);
            }
        }

        return null;
    }

    private static (int, int)? ScanBackward(Func<int, char> charAt, int start, char open, char close)
    {
        var depth = 0;
        var end = Math.Max(0, start - MaxScan);
        for (var i = start; i >= end; i--)
        {
            var c = charAt(i);
            if (c == close)
            {
                depth++;
            }
            else if (c == open && --depth == 0)
            {
                return (i, start);
            }
        }

        return null;
    }
}
