using System.Windows.Media;
using CodeEditor.Core.Completion;
using CodeEditor.ViewModels;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace CodeEditor.Services;

/// <summary>
/// One AvalonEdit completion entry backed by a Roslyn item. Committing asks the
/// language service for the exact text change (display text is not always the
/// insertion text) and replaces the live completion segment with it.
/// </summary>
public sealed class RoslynCompletionData : ICompletionData
{
    private readonly CompletionItemInfo _item;
    private readonly DocumentViewModel _document;

    public RoslynCompletionData(CompletionItemInfo item, DocumentViewModel document)
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
        string? newText = null;
        try
        {
            // Fast (no semantic recompute) and safe to block on: the service does its
            // work on the thread pool with ConfigureAwait(false) throughout.
            newText = _document.GetCompletionChangeAsync(_item.Index).GetAwaiter().GetResult()?.NewText;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            // Fall back to the display text below.
        }

        textArea.Document.Replace(completionSegment, newText ?? _item.DisplayText);
    }
}
