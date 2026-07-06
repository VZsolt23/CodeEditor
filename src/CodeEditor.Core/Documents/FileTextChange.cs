namespace CodeEditor.Core.Documents;

/// <summary>
/// A whole-file text replacement produced by a workspace-wide operation
/// (e.g. Rename Symbol). The consumer applies it to an open editor buffer
/// or writes it to disk.
/// </summary>
/// <param name="FilePath">Absolute path of the affected file.</param>
/// <param name="NewText">The complete new content of the file.</param>
public sealed record FileTextChange(string FilePath, string NewText);
