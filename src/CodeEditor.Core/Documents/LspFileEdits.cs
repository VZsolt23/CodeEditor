namespace CodeEditor.Core.Documents;

/// <summary>
/// A range-based text edit using 0-based line/character positions (LSP convention).
/// </summary>
/// <param name="StartLine">0-based start line.</param>
/// <param name="StartChar">0-based start character (UTF-16 code units).</param>
/// <param name="EndLine">0-based end line.</param>
/// <param name="EndChar">0-based end character (exclusive).</param>
/// <param name="NewText">Replacement text.</param>
public sealed record LspRangeEdit(int StartLine, int StartChar, int EndLine, int EndChar, string NewText);

/// <summary>
/// The edits to apply to one file, from a workspace-wide LSP operation (e.g. rename).
/// </summary>
/// <param name="FilePath">Absolute path of the affected file.</param>
/// <param name="Edits">Non-overlapping range edits for the file.</param>
public sealed record LspFileEdits(string FilePath, IReadOnlyList<LspRangeEdit> Edits);
