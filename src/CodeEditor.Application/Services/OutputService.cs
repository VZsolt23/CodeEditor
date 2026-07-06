using CodeEditor.Application.Interfaces;

namespace CodeEditor.Application.Services;

/// <summary>
/// In-memory implementation of <see cref="IOutputService"/>. Channels keep a
/// bounded backlog so long sessions cannot grow memory without limit.
/// </summary>
public sealed class OutputService : IOutputService
{
    private readonly object _lock = new();
    private readonly List<IOutputChannel> _channels = [];

    public event EventHandler<IOutputChannel>? ChannelAdded;

    public IReadOnlyList<IOutputChannel> Channels
    {
        get
        {
            lock (_lock)
            {
                return [.. _channels];
            }
        }
    }

    public IOutputChannel GetOrCreateChannel(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        OutputChannel created;
        lock (_lock)
        {
            var existing = _channels.FirstOrDefault(channel =>
                string.Equals(channel.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            created = new OutputChannel(name);
            _channels.Add(created);
        }

        ChannelAdded?.Invoke(this, created);
        return created;
    }

    private sealed class OutputChannel(string name) : IOutputChannel
    {
        private const int MaxLines = 5000;

        private readonly object _lock = new();
        private readonly Queue<string> _lines = new();

        public string Name { get; } = name;

        public event EventHandler<string>? LineAppended;

        public event EventHandler? Cleared;

        public void AppendLine(string line)
        {
            ArgumentNullException.ThrowIfNull(line);

            lock (_lock)
            {
                _lines.Enqueue(line);
                while (_lines.Count > MaxLines)
                {
                    _lines.Dequeue();
                }
            }

            LineAppended?.Invoke(this, line);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _lines.Clear();
            }

            Cleared?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<string> GetSnapshot()
        {
            lock (_lock)
            {
                return [.. _lines];
            }
        }
    }
}
