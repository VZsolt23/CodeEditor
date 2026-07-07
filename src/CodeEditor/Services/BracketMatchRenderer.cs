using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CodeEditor.Services;

/// <summary>
/// AvalonEdit background renderer that boxes the matched bracket pair. Only the
/// two single-character positions in view build geometry, so it is cheap.
/// </summary>
public sealed class BracketMatchRenderer : IBackgroundRenderer
{
    /// <summary>The (open, close) offsets to highlight, or null for none.</summary>
    public (int Open, int Close)? Brackets { get; set; }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Brackets is not { } pair || textView.Document is null || !textView.VisualLinesValid)
        {
            return;
        }

        var fill = GetThemeBrush("Brush.Editor.BracketMatch");
        var border = GetThemeBrush("Brush.Border.Strong") is { } stroke ? new Pen(stroke, 1) : null;
        border?.Freeze();

        DrawBracket(textView, drawingContext, pair.Open, fill, border);
        DrawBracket(textView, drawingContext, pair.Close, fill, border);
    }

    private static void DrawBracket(TextView textView, DrawingContext drawingContext, int offset, Brush? fill, Pen? border)
    {
        if (offset < 0 || offset >= textView.Document.TextLength)
        {
            return;
        }

        var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
        builder.AddSegment(textView, new TextSegment { StartOffset = offset, Length = 1 });

        if (builder.CreateGeometry() is { } geometry)
        {
            drawingContext.DrawGeometry(fill, border, geometry);
        }
    }

    private static Brush? GetThemeBrush(string key)
        => System.Windows.Application.Current.TryFindResource(key) as Brush;
}
