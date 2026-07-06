using System.Windows;
using System.Windows.Media;
using CodeEditor.Core.Diagnostics;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CodeEditor.Services;

/// <summary>
/// AvalonEdit background renderer drawing squiggly underlines beneath
/// diagnostic spans, colored by severity via the theme's diagnostic brushes.
/// Offsets are derived from line/column on every draw and clamped, so briefly
/// stale diagnostics (between edit and re-analysis) cannot go out of range.
/// </summary>
public sealed class DiagnosticSquiggleRenderer : IBackgroundRenderer
{
    private const double SquiggleAmplitude = 1.6;
    private const double SquiggleStep = 3.0;
    private const double MinUnderlineWidth = 6.0;

    /// <summary>Diagnostics of the bound document; empty for none.</summary>
    public IReadOnlyList<DiagnosticItem> Diagnostics { get; set; } = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Diagnostics.Count == 0 || textView.Document is null || !textView.VisualLinesValid)
        {
            return;
        }

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0)
        {
            return;
        }

        var firstVisibleLine = visualLines[0].FirstDocumentLine.LineNumber;
        var lastVisibleLine = visualLines[^1].LastDocumentLine.LineNumber;
        var document = textView.Document;
        var pens = new Dictionary<DiagnosticSeverity, Pen?>();

        foreach (var diagnostic in Diagnostics)
        {
            if (diagnostic.Line < Math.Max(1, firstVisibleLine)
                || diagnostic.Line > lastVisibleLine
                || diagnostic.Line > document.LineCount)
            {
                continue;
            }

            if (!pens.TryGetValue(diagnostic.Severity, out var pen))
            {
                pen = CreatePen(diagnostic.Severity);
                pens[diagnostic.Severity] = pen;
            }

            if (pen is null)
            {
                continue;
            }

            var line = document.GetLineByNumber(diagnostic.Line);
            var start = line.Offset + Math.Clamp(diagnostic.Column - 1, 0, line.Length);
            var end = Math.Min(start + Math.Max(diagnostic.Length, 1), line.EndOffset);
            if (end <= start)
            {
                // Diagnostic at/after the end of the line: underline the last character.
                start = Math.Max(line.Offset, line.EndOffset - 1);
                end = line.EndOffset;
                if (end <= start)
                {
                    continue;
                }
            }

            var segment = new TextSegment { StartOffset = start, EndOffset = end };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                DrawSquiggle(drawingContext, pen, rect);
            }
        }
    }

    private static void DrawSquiggle(DrawingContext drawingContext, Pen pen, Rect rect)
    {
        var left = rect.Left;
        var right = Math.Max(rect.Right, left + MinUnderlineWidth);
        var baseline = rect.Bottom - 1;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(left, baseline), isFilled: false, isClosed: false);
            var up = true;
            for (var x = left + SquiggleStep; x < right + SquiggleStep; x += SquiggleStep)
            {
                context.LineTo(
                    new Point(Math.Min(x, right), up ? baseline - SquiggleAmplitude : baseline),
                    isStroked: true,
                    isSmoothJoin: false);
                up = !up;
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Pen? CreatePen(DiagnosticSeverity severity)
    {
        var key = severity switch
        {
            DiagnosticSeverity.Error => "Brush.Diagnostic.Error",
            DiagnosticSeverity.Warning => "Brush.Diagnostic.Warning",
            _ => "Brush.Diagnostic.Info",
        };

        if (System.Windows.Application.Current.TryFindResource(key) is not Brush brush)
        {
            return null;
        }

        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }
}
