using System.Windows.Media;
using CodeEditor.Core.Documents;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CodeEditor.Services;

/// <summary>
/// AvalonEdit background renderer that paints find/replace matches: all matches
/// with the highlight brush, the current match with a stronger one. Only spans
/// inside the visible region build geometry, so large match sets stay cheap.
/// </summary>
public sealed class SearchHighlightRenderer : IBackgroundRenderer
{
    /// <summary>Matches to paint; empty for none.</summary>
    public IReadOnlyList<TextSpan> Highlights { get; set; } = [];

    /// <summary>The match to paint with the current-match brush.</summary>
    public TextSpan? CurrentHighlight { get; set; }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Highlights.Count == 0 || textView.Document is null || !textView.VisualLinesValid)
        {
            return;
        }

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0)
        {
            return;
        }

        var viewStart = visualLines[0].FirstDocumentLine.Offset;
        var viewEnd = visualLines[^1].LastDocumentLine.EndOffset;
        var highlightBrush = GetThemeBrush("Brush.Editor.FindMatch.Highlight");
        var currentBrush = GetThemeBrush("Brush.Editor.FindMatch.Current");

        foreach (var span in Highlights)
        {
            // Skip spans outside the viewport or stale offsets past the document end.
            if (span.Start > viewEnd || span.End < viewStart || span.End > textView.Document.TextLength)
            {
                continue;
            }

            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = span.Start, Length = span.Length });

            if (builder.CreateGeometry() is { } geometry)
            {
                var isCurrent = CurrentHighlight is { } current && current == span;
                drawingContext.DrawGeometry(isCurrent ? currentBrush : highlightBrush, null, geometry);
            }
        }
    }

    private static Brush? GetThemeBrush(string key)
        => System.Windows.Application.Current.TryFindResource(key) as Brush;
}
