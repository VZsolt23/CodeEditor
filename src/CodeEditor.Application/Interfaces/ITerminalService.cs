namespace CodeEditor.Application.Interfaces;

/// <summary>
/// One hosted shell process behind the Terminal panel. Output arrives as raw
/// text chunks (not lines, so prompts without a trailing newline show up);
/// events may fire on any thread.
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>File name of the shell executable (e.g. "cmd.exe").</summary>
    string ShellName { get; }

    /// <summary>Whether the shell process is still alive.</summary>
    bool IsRunning { get; }

    /// <summary>Raised for each chunk of stdout/stderr text. May fire on any thread.</summary>
    event EventHandler<string>? OutputReceived;

    /// <summary>Raised once when the shell process exits, with its exit code. May fire on any thread.</summary>
    event EventHandler<int>? Exited;

    /// <summary>Sends one input line (a command) to the shell.</summary>
    void WriteLine(string text);

    /// <summary>Terminates the shell process tree.</summary>
    void Kill();
}

/// <summary>
/// Starts terminal sessions hosting the configured shell.
/// </summary>
public interface ITerminalService
{
    /// <summary>
    /// Starts a new shell session in <paramref name="workingDirectory"/> (falls back
    /// to the user profile when null or missing).
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The shell executable could not be started.</exception>
    ITerminalSession Start(string? workingDirectory);
}
