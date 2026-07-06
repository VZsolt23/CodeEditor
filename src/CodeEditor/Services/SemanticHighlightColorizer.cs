using System.Windows.Media;
using CodeEditor.Core.Documents;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CodeEditor.Services;

/// <summary>
/// Line transformer applying semantic classification colors over the static
/// syntax highlighting (it runs after AvalonEdit's highlighting colorizer, so
/// its foregrounds win where spans are classified). Spans must be sorted by
/// offset; stale spans (between an edit and re-classification) are clamped to
/// line bounds, so brief drift cannot throw.
/// </summary>
public sealed class SemanticHighlightColorizer : DocumentColorizingTransformer
{
    private readonly Dictionary<string, Brush?> _brushCache = [];

    private IReadOnlyList<ClassifiedSpanInfo> _spans = [];

    /// <summary>Classification spans of the bound document, sorted by start offset.</summary>
    public IReadOnlyList<ClassifiedSpanInfo> Spans
    {
        get => _spans;
        set
        {
            _spans = value;
            // Brushes are theme resources; re-resolving per update keeps theme switches fresh.
            _brushCache.Clear();
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Spans.Count == 0)
        {
            return;
        }

        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;

        for (var i = FindFirstSpanIndex(lineStart); i < Spans.Count && Spans[i].Start < lineEnd; i++)
        {
            var span = Spans[i];
            var start = Math.Max(span.Start, lineStart);
            var end = Math.Min(span.Start + span.Length, lineEnd);
            if (start >= end)
            {
                continue;
            }

            if (GetBrush(span.Classification) is { } brush)
            {
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
            }
        }
    }

    /// <summary>Binary search for the first span that may intersect <paramref name="lineStart"/>.</summary>
    private int FindFirstSpanIndex(int lineStart)
    {
        int low = 0, high = Spans.Count - 1, result = Spans.Count;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (Spans[middle].Start + Spans[middle].Length > lineStart)
            {
                result = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return result;
    }

    private Brush? GetBrush(string classification)
    {
        if (_brushCache.TryGetValue(classification, out var cached))
        {
            return cached;
        }

        var brush = GetBrushKey(classification) is { } key
            ? System.Windows.Application.Current.TryFindResource(key) as Brush
            : null;
        _brushCache[classification] = brush;
        return brush;
    }

    /// <summary>Maps Roslyn classification names to theme brush keys (null keeps the default color).</summary>
    private static string? GetBrushKey(string classification) => classification switch
    {
        "keyword" => "Brush.Syntax.Keyword",
        "keyword - control" => "Brush.Syntax.ControlKeyword",
        "preprocessor keyword" => "Brush.Syntax.Preprocessor",
        "class name" or "struct name" or "interface name" or "enum name" or "delegate name"
            or "record class name" or "record struct name" or "type parameter name" => "Brush.Syntax.Type",
        "method name" or "extension method name" => "Brush.Syntax.Method",
        "parameter name" or "local name" => "Brush.Syntax.Variable",
        "string" or "verbatim string" or "string - escape character" => "Brush.Syntax.String",
        "number" => "Brush.Syntax.Number",
        _ when classification.StartsWith("comment", StringComparison.Ordinal)
               || classification.StartsWith("xml doc comment", StringComparison.Ordinal) => "Brush.Syntax.Comment",
        _ => null,
    };
}
