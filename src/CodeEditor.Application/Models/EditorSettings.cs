namespace CodeEditor.Application.Models;

/// <summary>
/// User-configurable editor settings, persisted as JSON.
/// </summary>
public sealed class EditorSettings
{
    /// <summary>Identifier of the active theme (see IThemeService).</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Font family used by the text editor.</summary>
    public string FontFamily { get; set; } = "Cascadia Code";

    /// <summary>Font size (in WPF device-independent units) used by the text editor.</summary>
    public double FontSize { get; set; } = 13;

    /// <summary>Number of columns a tab occupies.</summary>
    public int TabWidth { get; set; } = 4;

    /// <summary>Whether long lines wrap instead of scrolling horizontally.</summary>
    public bool WordWrap { get; set; }

    /// <summary>Whether line numbers are shown in the editor margin.</summary>
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Whether dirty file-backed documents are saved automatically.</summary>
    public bool AutoSave { get; set; }

    /// <summary>Interval, in seconds, between auto-save passes when <see cref="AutoSave"/> is enabled.</summary>
    public int AutoSaveIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum number of entries kept in the recent files list.</summary>
    public int RecentFilesLimit { get; set; } = 10;

    /// <summary>Most-recently-used files, newest first.</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Folder names hidden from the workspace explorer (compared case-insensitively).</summary>
    public List<string> ExplorerExcludedFolders { get; set; } = ["bin", "obj", "node_modules", ".git", ".vs"];

    /// <summary>Shell executable hosted by the terminal panel.</summary>
    public string TerminalShell { get; set; } = "cmd.exe";

    /// <summary>Command that starts the TypeScript/JavaScript language server (spawned with --stdio).</summary>
    public string TypeScriptServerCommand { get; set; } = "typescript-language-server";

    /// <summary>Command that starts the HTML language server (spawned with --stdio).</summary>
    public string HtmlServerCommand { get; set; } = "vscode-html-language-server";

    /// <summary>Command that starts the CSS/SCSS/Less language server (spawned with --stdio).</summary>
    public string CssServerCommand { get; set; } = "vscode-css-language-server";

    /// <summary>Command that starts the JSON language server (spawned with --stdio).</summary>
    public string JsonServerCommand { get; set; } = "vscode-json-language-server";

    /// <summary>Whether the last session (folder, expanded nodes, open tabs) is restored at startup.</summary>
    public bool RestoreSession { get; set; } = true;

    /// <summary>State captured at exit for session restore. Never null (a hand-edited null falls back to empty).</summary>
    public SessionState Session
    {
        get;
        set => field = value ?? new SessionState();
    } = new();
}
