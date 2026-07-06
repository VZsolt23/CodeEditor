namespace CodeEditor.Application.Models;

/// <summary>
/// Workspace/editor state captured at exit and restored at the next startup.
/// All paths are absolute; entries that no longer exist are skipped on restore.
/// </summary>
public sealed class SessionState
{
    /// <summary>Root of the workspace that was open, or null when none was.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Explorer folders that were expanded (parents always precede children on restore).</summary>
    public List<string> ExpandedFolders { get; set; } = [];

    /// <summary>Files that were open as tabs, in tab order.</summary>
    public List<string> OpenFiles { get; set; } = [];

    /// <summary>The file whose tab was active, or null.</summary>
    public string? ActiveFile { get; set; }
}
