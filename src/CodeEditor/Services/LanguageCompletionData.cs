using System.Windows.Media;
using CodeEditor.Core.Completion;
using CodeEditor.ViewModels;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace CodeEditor.Services;

/// <summary>
/// One AvalonEdit completion entry backed by a language-service item. On commit
/// it uses the item's own insert text when present (LSP); otherwise it asks the
/// service to resolve the exact change by index (Roslyn — display text is not
/// always the insertion text). The live completion segment is then replaced.
/// </summary>
public sealed class LanguageCompletionData : ICompletionData
{
    private readonly CompletionItemInfo _item;
    private readonly DocumentViewModel _document;

    public LanguageCompletionData(CompletionItemInfo item, DocumentViewModel document)
    {
        _item = item;
        _document = document;
    }

    public ImageSource? Image => null;

    /// <summary>Used by AvalonEdit for prefix filtering and selection.</summary>
    public string Text => _item.FilterText;

    public object Content => _item.DisplayText;

    public object? Description => _item.Kind.Length > 0 ? _item.Kind : null;

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var newText = _item.InsertText;
        if (newText is null)
        {
            try
            {
                // Roslyn: resolve the change by index. Fast (no semantic recompute) and
                // safe to block on — the service works on the thread pool throughout.
                newText = _document.GetCompletionChangeAsync(_item.Index).GetAwaiter().GetResult()?.NewText;
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
            {
                // Fall back to the display text below.
            }
        }

        textArea.Document.Replace(completionSegment, newText ?? _item.DisplayText);
    }
}
