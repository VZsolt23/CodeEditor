namespace CodeEditor.Application.Interfaces;

/// <summary>
/// A named output stream shown in the Output panel (e.g. "Log", later per
/// language server). Thread-safe; events may be raised on any thread.
/// </summary>
public interface IOutputChannel
{
    /// <summary>Display name of the channel.</summary>
    string Name { get; }

    /// <summary>Raised after a line is appended. May fire on any thread.</summary>
    event EventHandler<string>? LineAppended;

    /// <summary>Raised after the channel content is cleared. May fire on any thread.</summary>
    event EventHandler? Cleared;

    /// <summary>Appends one line to the channel, trimming the oldest lines past the cap.</summary>
    void AppendLine(string line);

    /// <summary>Removes all lines.</summary>
    void Clear();

    /// <summary>Returns a copy of the current content.</summary>
    IReadOnlyList<string> GetSnapshot();
}

/// <summary>
/// Registry of output channels backing the Output panel.
/// </summary>
public interface IOutputService
{
    /// <summary>All channels, in creation order.</summary>
    IReadOnlyList<IOutputChannel> Channels { get; }

    /// <summary>Raised when a new channel is created. May fire on any thread.</summary>
    event EventHandler<IOutputChannel>? ChannelAdded;

    /// <summary>Returns the channel with the given name (case-insensitive), creating it if needed.</summary>
    IOutputChannel GetOrCreateChannel(string name);
}
