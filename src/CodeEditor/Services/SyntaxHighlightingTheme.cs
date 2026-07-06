using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace CodeEditor.Services;

/// <summary>
/// Recolors AvalonEdit's built-in .xshd highlighting definitions to the active
/// Ember palette. The shipped definitions use light-oriented token colors, so
/// without this a file shows off-theme colors at first paint — until Roslyn
/// semantic classification takes over for C# (a short delay after the workspace
/// loads), or permanently for non-C# files. Applied at startup (before any file
/// opens) and on every theme change. Tokens are classified by the color's Name,
/// so re-runs are idempotent — the original color values are never read back.
/// </summary>
public static class SyntaxHighlightingTheme
{
    /// <summary>Raised after definitions are recolored, so open editors can repaint.</summary>
    public static event EventHandler? Recolored;

    /// <summary>Recolors every loaded highlighting definition from the current theme resources.</summary>
    public static void Apply()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var palette = new Palette(
            Keyword: Resolve(app, "Brush.Syntax.Keyword"),
            Control: Resolve(app, "Brush.Syntax.ControlKeyword"),
            Preprocessor: Resolve(app, "Brush.Syntax.Preprocessor"),
            Type: Resolve(app, "Brush.Syntax.Type"),
            Method: Resolve(app, "Brush.Syntax.Method"),
            Variable: Resolve(app, "Brush.Syntax.Variable"),
            String: Resolve(app, "Brush.Syntax.String"),
            Number: Resolve(app, "Brush.Syntax.Number"),
            Comment: Resolve(app, "Brush.Syntax.Comment"),
            Text: Resolve(app, "Brush.Editor.Foreground"));

        foreach (var definition in HighlightingManager.Instance.HighlightingDefinitions)
        {
            foreach (var color in definition.NamedHighlightingColors)
            {
                color.Foreground = new SimpleHighlightingBrush(Classify(color.Name, palette));
            }
        }

        Recolored?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Maps a highlighting color's name to an Ember token color.</summary>
    private static Color Classify(string? name, Palette p)
    {
        if (string.IsNullOrEmpty(name))
        {
            return p.Text;
        }

        var n = name.ToLowerInvariant();

        if (n.Contains("comment"))
        {
            return p.Comment;
        }

        if (n.Contains("string") || n.Contains("char"))
        {
            return p.String;
        }

        if (n.Contains("number") || n.Contains("digit"))
        {
            return p.Number;
        }

        if (n.Contains("preprocessor") || n.Contains("region"))
        {
            return p.Preprocessor;
        }

        if (n.Contains("exception") || n.Contains("goto") || n.Contains("loop") || n.Contains("jump") || n.Contains("control"))
        {
            return p.Control;
        }

        // Checked before "type": names like "TypeKeywords" (int, class) are keywords.
        if (n.Contains("keyword") || n.Contains("modifier") || n.Contains("visibility") || n.Contains("access"))
        {
            return p.Keyword;
        }

        if (n.Contains("method") || n.Contains("function"))
        {
            return p.Method;
        }

        if (n.Contains("type") || n.Contains("class") || n.Contains("tagname") || n.Contains("element"))
        {
            return p.Type;
        }

        if (n.Contains("attribute") || n.Contains("property"))
        {
            return p.Variable;
        }

        // Punctuation, operators, braces, and anything unrecognized render as plain text.
        return p.Text;
    }

    private static Color Resolve(System.Windows.Application app, string key)
        => app.TryFindResource(key) is SolidColorBrush brush ? brush.Color : Colors.Gray;

    private readonly record struct Palette(
        Color Keyword, Color Control, Color Preprocessor, Color Type, Color Method,
        Color Variable, Color String, Color Number, Color Comment, Color Text);
}
