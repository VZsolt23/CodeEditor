namespace CodeEditor.Core.Completion;

/// <summary>
/// One completion suggestion, in provider order.
/// </summary>
/// <param name="Index">Position in the provider's list; used to request the item's text change on commit.</param>
/// <param name="DisplayText">Text shown in the completion list (may include suffixes like "&lt;&gt;").</param>
/// <param name="FilterText">Text the typed prefix is matched against.</param>
/// <param name="SortText">Provider sort key.</param>
/// <param name="Kind">Item kind tag (e.g. "Method", "Class", "Keyword") for icons.</param>
public sealed record CompletionItemInfo(int Index, string DisplayText, string FilterText, string SortText, string Kind);
