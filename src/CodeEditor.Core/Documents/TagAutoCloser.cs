namespace CodeEditor.Core.Documents;

/// <summary>
/// Decides whether a just-closed markup tag should be auto-completed with a
/// matching closing tag, and what that tag's name is. Pure parsing so the editor
/// only supplies the text between '&lt;' and '&gt;'.
/// </summary>
public static class TagAutoCloser
{
    // HTML elements that are self-closing by definition — never auto-close these.
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "keygen", "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>Whether <paramref name="name"/> is an HTML void element (no closing tag). Case-insensitive.</summary>
    public static bool IsVoidElement(string name) => VoidElements.Contains(name);

    /// <summary>
    /// Given <paramref name="inner"/> (the text between a tag's '&lt;' and '&gt;'),
    /// returns the tag name to close, or null when no closing tag should be inserted:
    /// closing tags (<c>/div</c>), self-closing tags (<c>br/</c>), comments and
    /// declarations (<c>!</c>/<c>?</c>), void HTML elements, and invalid names.
    /// </summary>
    public static string? GetOpenTagName(string inner, bool isHtml)
    {
        if (string.IsNullOrWhiteSpace(inner))
        {
            return null;
        }

        var text = inner.TrimStart();
        if (text.Length == 0 || text[0] is '/' or '!' or '?')
        {
            return null;
        }

        if (inner.TrimEnd().EndsWith('/'))
        {
            return null;
        }

        if (!(char.IsLetter(text[0]) || text[0] is '_' or ':'))
        {
            return null;
        }

        var length = 0;
        while (length < text.Length && IsNameChar(text[length]))
        {
            length++;
        }

        var name = text[..length];
        if (name.Length == 0 || (isHtml && VoidElements.Contains(name)))
        {
            return null;
        }

        return name;

        static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' or ':' or '.';
    }
}
