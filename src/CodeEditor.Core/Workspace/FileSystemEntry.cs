namespace CodeEditor.Core.Workspace;

/// <summary>
/// A single file or directory inside a workspace, as shown in the explorer.
/// </summary>
/// <param name="Name">The file or directory name without its path.</param>
/// <param name="FullPath">The absolute path of the entry.</param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
public sealed record FileSystemEntry(string Name, string FullPath, bool IsDirectory);
